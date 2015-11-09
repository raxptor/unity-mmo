using System;
using System.Collections.Generic;
using netki;

namespace UnityMMO
{
	public class ServerZombieAIController : Controller
	{
		const float height_y = 2.0f;
	
		// params
		public string m_Character;
		public float m_HP;
		public float m_PatrolRadius;
		public float m_SearchRadius;
		public float m_GiveUpRange;
		public float m_MoveSpeed;
		public float m_MinSpawnTime;
		public float m_MaxSpawnTime;
		public float m_EngageDistance = 1.1f;
		public uint m_PathCooldown;

		public struct AttackDef
		{
			public string AnimTrigger;
			public float Duration;
			public float Cooldown;
			public uint AttackMin, AttackMax;
		};

		public AttackDef[] m_Attacks;

		//
		Random m_random = new Random();

		public void Parse(Bitstream.Buffer b)
		{
			m_Character = Bitstream.ReadStringDumb(b);
			m_HP = Bitstream.ReadFloat(b);
			m_PatrolRadius = Bitstream.ReadFloat(b);
			m_SearchRadius = Bitstream.ReadFloat(b);
			m_GiveUpRange = Bitstream.ReadFloat(b);
			m_MoveSpeed = Bitstream.ReadFloat(b);
			m_MinSpawnTime = Bitstream.ReadFloat(b);
			m_MaxSpawnTime = Bitstream.ReadFloat(b);

			m_Attacks = new AttackDef[Bitstream.ReadCompressedUint(b)];
			for (int i = 0; i < m_Attacks.Length; i++)
			{
				m_Attacks[i].AnimTrigger = Bitstream.ReadStringDumb(b);
				m_Attacks[i].Duration = Bitstream.ReadFloat(b);
				m_Attacks[i].Cooldown = Bitstream.ReadFloat(b);
				m_Attacks[i].AttackMin = Bitstream.ReadCompressedUint(b);
				m_Attacks[i].AttackMax = Bitstream.ReadCompressedUint(b);
			}
		}

		class Path
		{
			public Vector3[] track;
			public int next;
			public float time;
			// just for user.
			public bool is_subpath;
		};

		class Data
		{
			public enum State
			{
				IDLE,
				PATROL,
				CHASE,
				ATTACK,
			}

			public Data()
			{
				GroundedOnPoly = -1;
			}

			public int GroundedOnPoly;
			public ushort Island;
			public uint LastControllerUpdate;
			public ServerCharacter Target;

			public Path CurrentPath;
			public bool TargetPathIsComplete;
			public Vector3 TargetPathTarget;

			public float[] AttackCooldown;
			public float AttackTimer;

			public float SpawnTimer;
			public float CorpseTimer;

			public float HitCooldown;
			public uint PathCooldown;
			public uint SpottedSoundCooldown;
	
			public State CurState;
		};

		public const uint PathCooldownIterations = 300;

		public static float _Dist(Vector3 a, Vector3 b)
		{
			Vector3 d;
			d.x = b.x - a.x;
			d.y = b.y - a.y;
			d.z = b.z - a.z;
			return (float) Math.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z);
		}

		public static float _Dist2D(Vector3 a, Vector3 b)
		{
			Vector3 d;
			d.x = b.x - a.x;
			d.z = b.z - a.z;
			return (float) Math.Sqrt(d.x * d.x + d.z * d.z);
		}

		private void Fall(ServerCharacter character, Data d, float dt)
		{
			float ground_y;
			int idx;
			if (!character.World._navMVP.GetPoly(character.Position, out idx, out ground_y))
			{
				SnapToNavMesh(character, d);
				return;
			}

			if (character.Position.y > ground_y)
			{
				const float g = 9.80f;
				character.Velocity.y -= dt * g;
				character.Position.y += dt * character.Velocity.y - 0.5f * dt * dt * g;
				if (character.Position.y <= ground_y)
				{
					character.Position.y = ground_y;
					character.Velocity.y = 0;
					d.GroundedOnPoly = idx;
					d.Island = character.World._navMVP.m_island[idx];
					UnityMMO.Debug.Log("Fell to point y=" + ground_y + " island=" + d.Island);
				}
			}
		}

		private ServerCharacter SpotTarget(ServerCharacter character, Data d)
		{
			ServerCharacter res = null;
			WorldServer w = character.World;

			foreach (ServerCharacter ch in w._activeCharacters)
			{
				float closest = 0;
				if (ch.Data.HumanControllable)
				{
					if (!ch.Alive())
						continue;
				
					int idx;
					float ground_y;
					if (!character.World._navMVP.GetPoly(character.Position, out idx, out ground_y))
					{
						continue;
					}

					float dist = _Dist(ch.Position, character.Position);
					float dist2 = _Dist(ch.Position, character.Data.DefaultSpawnPos);
					if (dist2 > m_SearchRadius + m_PatrolRadius)
						continue;
					
					if (dist < closest || closest == 0 && dist < m_SearchRadius)
					{
						closest = dist;
						res = ch;
					}
				}
			}
			return res;
		}

		Vector3 CurrentGoal(Path p)
		{
			int p0 = p.next;
			int p1 = p.next + 1;
			if (p1 < p.track.Length)
			{
				return p.track[p0] + p.time * (p.track[p1] - p.track[p0]);
			}
			else
			{
				return p.track[p.track.Length - 1];
			}	
		}

		bool AdvancePath(Path p, float dist)
		{
			int p0 = p.next;
			int p1 = p.next + 1;
			if (p1 >= p.track.Length)
				return false;

			float amt = 1.0f;
			float d = _Dist2D(p.track[p0], p.track[p1]);
			if (d > 0.01f)
			{
				amt = dist / d;
			}

			p.time += amt;

			if (p.time >= 1.0)
			{
				p.time = 0.0f;
				p.next++;
			}
			return true;
		}

		private float TurnTowards(ServerCharacter character, Data d, Vector3 toTargetNorm, float dt)
		{
			const float tau = 3.1415f * 2.0f;

			Vector3 heading = ServerCharacter.HeadingVector(character.Heading);
			float dot = Vector3.Dot(heading, new Vector3(toTargetNorm.x, 0, toTargetNorm.z));
			float angle = (float)Math.Acos(dot);

			float turnAmt = Math.Abs(angle);
			int turnDir = Math.Sign((float)Vector3.Cross(heading, toTargetNorm).y);

			if (turnDir == 0)
				turnDir = 1;

			float turn = dt * 3.1415f;
			if (turn > turnAmt)
				turn = turnAmt;

			if (turnAmt > 0.05f)
				character.Heading += turn * turnDir;

			while (character.Heading > tau)
				character.Heading -= tau;
			while (character.Heading < 0)
				character.Heading += tau;

			return Vector3.Dot(toTargetNorm, ServerCharacter.HeadingVector(character.Heading));
		}

		private bool FollowPath(ServerCharacter character, Data d, float dt, Path p, out NavHelper.HitInfo hi)
		{
			Vector3 pathTarget = CurrentGoal(p);

			hi.hit = null;
			hi.t = 0;

			while (dt > 0.0f && _Dist2D(character.Position, pathTarget) < 0.25f * m_MoveSpeed)
			{
				if (AdvancePath(p, dt * m_MoveSpeed))
					pathTarget = CurrentGoal(p);
				else
					break;
			}
				
			Vector3 toTarget = pathTarget - character.Position;

			// oblivious to y
			toTarget.y = 0;

			float toTargetDsq = Vector3.Dot(toTarget, toTarget);
			bool ok = true;

			if (toTargetDsq > 0.00001f)
			{
				float toTargetD = (float)Math.Sqrt(toTargetDsq);
				Vector3 toTargetNorm = (1.0f / (toTargetD) * toTarget);

				float headingAmt = TurnTowards(character, d, toTargetNorm, dt);
				if (headingAmt < 0)
				{
					headingAmt = 0;
				}
				else
				{
					headingAmt = 0.5f + 0.5f * headingAmt;
				}

				float lastSpeed = (float) Math.Sqrt(Vector3.Dot(character.Velocity, character.Velocity));
				float acceleration = m_MoveSpeed * 0.40f + 0.40f * lastSpeed; 

				float move = headingAmt * dt * m_MoveSpeed;
				if (move > toTargetD)
				{
					move = toTargetD;
				}

				Vector3 next = character.Position + move * toTargetNorm;

				if (!NavHelper.TestCharacterMove(character, next, character.World._activeCharacters, out hi))
				{
					character.Position = next;
				}
				else
				{
					character.Position = character.Position + hi.t * move * toTargetNorm;
					if (hi.t < 0.001f)
						ok = false;
				}

				character.Velocity = (1.0f / dt) * move * toTargetNorm;

				SnapToNavMesh(character, d);
			}

			if (_Dist(character.Position, p.track[p.track.Length-1]) < 0.01f)
			{
				return false;
			}

			return ok;
		}

		private void SnapToNavMesh(ServerCharacter character, Data d)
		{
			NavMeshMVP nav = character.World._navMVP;
			int idx;
			float y;
			if (!nav.GetPoly(character.Position, out idx, out y, d.Island))
			{
				if (!nav.GetPointInside(character.Position, d.Island, out character.Position))
				{
					Console.WriteLine("I am completeley lost! Unspawning.");
					character.Spawned = false;
				}
			}
			else
			{
				character.Position.y = y;
			}
		}

		private Path PlanPathTo(ServerCharacter character, Vector3 target)
		{
			Data d = character.ControllerData as Data;

			SnapToNavMesh(character, d);

			NavMeshMVP nav = character.World._navMVP;
			int idx;
			float y;

			// TODO: Code approximator make path instead.
			Random r = new Random();
			Vector3 tryend = target;
			tryend.y += 0.20f;

			for (int i = 0; i < 10; i++)
			{
				if (nav.GetPoly(tryend, out idx, out y))
				{
					Path p = new Path();
					p.track = nav.MakePath(character.Position, tryend, null, d.Island);
					if (p.track == null)
						continue;
					
					p.next = 0;
					p.time = 0;
					if (Math.Abs(p.track[0].y - character.Position.y) > 0.10f)
					{
						Console.WriteLine("height jump!");
					}
					return p;
				}
				tryend.x = target.x + (float)(r.NextDouble() - 0.5f) * 0.25f;
				tryend.z = target.z + (float)(r.NextDouble() - 0.5f) * 0.25f;
			}

			return null;
		}

		private void DoAttack(uint iteration, ServerCharacter me, Data d, ServerCharacter target)
		{
			if (m_Attacks.Length == 0)
				return;

			int which = m_random.Next(0, m_Attacks.Length);
			if (d.AttackCooldown[which] <= 0)
			{
				d.AttackCooldown[which] = m_Attacks[which].Cooldown;
				d.AttackTimer = m_Attacks[which].Duration;

				int dmg = m_random.Next((int)m_Attacks[which].AttackMin, (int)m_Attacks[which].AttackMax + 1);
				target.TakeDamage(dmg);
				me.AddAnimEvent(m_Attacks[which].AnimTrigger);
				me.AddSoundEvent("attack");

				Bitstream.Buffer hitBuf = Bitstream.Buffer.Make(new byte[256]);
				Bitstream.PutCompressedUint(hitBuf, 1); // type: hit
				Bitstream.PutCompressedUint(hitBuf, target.m_Id);
				NetUtil.PutScaledVec3(hitBuf, 0.001f, new Vector3(0, 1.8f, 0));
				Bitstream.PutCompressedInt(hitBuf, dmg);
				hitBuf.Flip();
				target.Events.Add(hitBuf);
			}
		}

		public void OnHit(uint iteration, ServerCharacter character, Entity inflictor,  string hitbox, int amount)
		{
			if (!character.Alive())
				return;
			
			Console.WriteLine("Hit on " + hitbox + "!");
			if (hitbox.Length > 0)
			{
				character.AddAnimEvent(hitbox);
			}

			Data ccd = character.ControllerData as Data;
			if (ccd != null)
			{
				ccd.HitCooldown = 0.30f; // cool down
				ccd.Target = inflictor as ServerCharacter;
				ccd.CurState = Data.State.ATTACK;
			}
		}

		public void ControlMe(uint iteration, ServerCharacter character)
		{
			if (!character.Spawned)
			{
				Data ccd = character.ControllerData as Data;
				if (ccd != null)
				{
					if (ccd.SpawnTimer > 0.0f)
					{
						float cdt = 0.001f * (iteration - ccd.LastControllerUpdate);
						ccd.LastControllerUpdate = iteration;
						ccd.SpawnTimer -= cdt;
						return;
					}
				}

				Debug.Log("Spawn: AI (" + m_Character + ") " + character.Data.DefaultSpawnPos.x + ":" + character.Data.DefaultSpawnPos.z);
				character.ResetFromData(character.Data);
				character.Position = character.Data.DefaultSpawnPos;
				character.Velocity.x = 0;
				character.Velocity.y = 0;
				character.Velocity.z = 0;
				character.CharacterTypeId = m_Character;
				character.Spawned = true;
				character.Dead = false;
				character.TimeOffset = 0;
				character.GotNew = true;

				Data nd = new Data();
				nd.GroundedOnPoly = -1;
				nd.LastControllerUpdate = iteration;
				nd.CurState = Data.State.IDLE;
				nd.AttackCooldown = new float[m_Attacks.Length];
				nd.SpawnTimer = m_MinSpawnTime + (float)m_random.NextDouble() * (m_MaxSpawnTime - m_MinSpawnTime);
				nd.CorpseTimer = 10.0f;
				character.ControllerData = nd;

				character.Velocity = new Vector3(0,0,0);
			}

			if (character.ControllerData == null)
				return;

			Data d = character.ControllerData as Data;
			float dt = 0.001f * (iteration - d.LastControllerUpdate);
			d.LastControllerUpdate = iteration;

			if (character.Dead)
			{
				character.Velocity = new Vector3(0, 0, 0);
				d.SpawnTimer -= dt;
				d.CorpseTimer -= dt;
				if (d.CorpseTimer < 0)
				{
					character.Spawned = false;
					character.GotNew = true;
				}
				return;
			}

			// -------------
			// Normal update

			if (d.GroundedOnPoly == -1)
			{
				Fall(character, d, dt);
				character.GotNew = true;
				return;
			}

			for (int i = 0; i < m_Attacks.Length; i++)
				d.AttackCooldown[i] -= dt;

			if (d.HitCooldown > 0)
			{
				d.HitCooldown -= dt;
				if (d.HitCooldown < 0)
				{
					d.HitCooldown = 0;
				}
				return;
			}

			if (d.GroundedOnPoly != -1)
			{
				switch (d.CurState)
				{
					case Data.State.IDLE:
						{
							character.Velocity = 0.20f * character.Velocity;
							d.CurrentPath = null;
							d.CurState = Data.State.PATROL;
							d.Target = null;
							break;
						}

					case Data.State.PATROL:
						{
							if (d.CurrentPath == null)
							{
								// Find a path to patrol.
								Vector3 patrol_pos = character.Data.DefaultSpawnPos;
								patrol_pos.x += (float)(2.0f * m_PatrolRadius * (m_random.NextDouble() - 0.5f));
								patrol_pos.z += (float)(2.0f * m_PatrolRadius * (m_random.NextDouble() - 0.5f));
								if (_Dist2D(patrol_pos, character.Position) > (m_PatrolRadius * 0.25f))
								{
									NavMeshMVP nav = character.World._navMVP;
									int poly;
									if (nav.GetPoly(patrol_pos, out poly, out patrol_pos.y, d.Island))
									{
										d.CurrentPath = PlanPathTo(character, patrol_pos);
										if (d.CurrentPath != null)
										{
											Console.WriteLine("I will patrol from " + character.Position + " to " + patrol_pos);
										}
									}
								}
							}

							if (d.CurrentPath != null)
							{
								NavHelper.HitInfo hi;
								if (!FollowPath(character, d, dt, d.CurrentPath, out hi))
								{
									d.CurrentPath = null;
									d.CurState = Data.State.IDLE;
								}
							}

							ServerCharacter potTarget = SpotTarget(character, d);
							if (potTarget != null)
							{
								if (d.Target == null && potTarget != null && _Dist(potTarget.Position, character.Position) > 1.5f)
								{
									if (iteration > d.SpottedSoundCooldown)
									{
										character.AddSoundEvent("spotted");
										// hax for now
										d.SpottedSoundCooldown = iteration + (uint)m_random.Next(2000, 15000);
									}
								}
									
								if (d.PathCooldown <= iteration)
								{
									d.PathCooldown = iteration + PathCooldownIterations;
									Path p = PlanPathTo(character, potTarget.Position);
									if (p != null && _Dist(p.track[p.track.Length-1], character.Position) > 0.10f)
									{
										Console.WriteLine("Going into chase mode!");
										d.Target = potTarget;
										d.TargetPathTarget = potTarget.Position;
										d.TargetPathIsComplete = potTarget.Position == p.track[p.track.Length - 1];
										d.CurrentPath = p;
										d.CurState = Data.State.CHASE;
									}
								}
							}
						}
						break;

					case Data.State.CHASE:
						{
							if (d.CurrentPath == null || d.Target == null || !d.Target.Alive())
							{
								Console.WriteLine("Target unspawned, lost target or it unspawned. Idling.");
								d.CurState = Data.State.IDLE;
								break;
							}

							if (_Dist2D(character.Position, character.Data.DefaultSpawnPos) > m_GiveUpRange)
							{
								Console.WriteLine("Not chasing; too far away");
								d.Target = null;
								d.CurState = Data.State.IDLE;
								break;
							}

							NavHelper.HitInfo hi;
							if (!FollowPath(character, d, dt, d.CurrentPath, out hi))
							{
								if (hi.hit != null && !d.CurrentPath.is_subpath)
								{
									// not finished because hit something.
									Vector3 norm = (hi.hit as ServerCharacter).Position - character.Position;
									Vector3 avoid = new Vector3(norm.z, 0, -norm.x);

									float dn = (float)Math.Sqrt(Vector3.Dot(norm, norm));
									float dist = (float)Math.Sqrt(Vector3.Dot(avoid, avoid));

									d.CurrentPath = PlanPathTo(character, character.Position + (dist / dn) * avoid + (-0.01f * norm));
									if (d.CurrentPath != null)
									{
										d.CurrentPath.is_subpath = true;
										Console.WriteLine("Replanned around obstacle with " + d.CurrentPath.track + " nodes");
									}
									else
									{
										Console.WriteLine("Not sure how to get around this..!");
										d.CurState = Data.State.IDLE;
									}
								}
								else
								{
									d.CurrentPath = PlanPathTo(character, d.Target.Position);
									Console.WriteLine("Chase path done, replanning new path");
									d.CurState = Data.State.IDLE;
								}
								break;
							}
	
							float dx = d.TargetPathTarget.x - d.Target.Position.x;
							float dz = d.TargetPathTarget.z - d.Target.Position.z;
							float mx = d.TargetPathTarget.x - character.Position.x;
							float mz = d.TargetPathTarget.z - character.Position.z;
							double dd = Math.Sqrt(dx * dx + dz * dz);
							double dm = Math.Sqrt(mx * mx + mz * mz);
							if (dd > dm * 0.20f)
							{
								if (dx * dx + dz * dz > 250.0f)
								{
									Debug.Log("Target ran away super far");
									d.CurState = Data.State.IDLE;
									break;
								}

								Path p = PlanPathTo(character, d.Target.Position);
								if (p != null)
								{
									Console.WriteLine("Upgraded path");
									d.CurrentPath = p;
									d.TargetPathTarget = d.Target.Position;
									d.TargetPathIsComplete = d.Target.Position == p.track[p.track.Length - 1];
								}
							}
						}

						if (_Dist2D(d.Target.Position, character.Position) < m_EngageDistance)
						{
							Console.WriteLine("Engaging in attack!");
							d.CurState = Data.State.ATTACK;
						}
						break;

					case Data.State.ATTACK:
						{
							if (!d.Target.Alive())
							{
								Console.WriteLine("Target died, going idle");
								d.CurState = Data.State.IDLE;
								break;
							}
				
							float angle = 1.0f;
							Vector3 diff = d.Target.Position - character.Position;
							diff.y = 0.0f;
							float diffD = (float) Math.Sqrt(Vector3.Dot(diff, diff));
							if (Vector3.Dot(diff, diff) > 0.01f)
							{
								Vector3 diffNorm = (1.0f / (float)diffD) * diff;
								angle = TurnTowards(character, d, diffNorm, dt);
							}

							if (diffD > (m_EngageDistance * 1.05f))
							{
								Console.WriteLine("Target moved away, getting closer");
								d.CurrentPath = PlanPathTo(character, d.Target.Position);
								d.CurState = Data.State.CHASE;
								break;
							}

							if (d.AttackTimer > 0)
							{
								d.AttackTimer -= dt;
								if (d.AttackTimer < 0)
									d.AttackTimer = 0;
							}

							if (angle > 0.20f)
							{
								if (d.AttackTimer == 0)
								{
									DoAttack(iteration, character, d, d.Target);
								}
							}
							else
							{
								Console.WriteLine("Attack state:" + diffD + " angle=" + angle);
							}
							break;
						}
				}
			}

				
			character.GotNew = true;
		}
	}
}
