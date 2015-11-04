using System;
using netki;

namespace UnityMMO
{
	public class ServerZombieAIController : Controller
	{
		const float height_y = 2.0f;
	
		// params
		public string m_Character;
		public float m_HP;
		public float m_Attack;
		public float m_PatrolRadius;
		public float m_SearchRadius;
		public float m_MoveSpeed;
		public float m_MinSpawnTime;
		public float m_MaxSpawnTime;
		public float m_EngageDistance = 1.5f;

		//
		Random m_random = new Random();

		public void Parse(Bitstream.Buffer b)
		{
			m_Character = Bitstream.ReadStringDumb(b);
			m_HP = Bitstream.ReadFloat(b);
			m_Attack = Bitstream.ReadFloat(b);
			m_PatrolRadius = Bitstream.ReadFloat(b);
			m_SearchRadius = Bitstream.ReadFloat(b);
			m_MoveSpeed = Bitstream.ReadFloat(b);
			m_MinSpawnTime = Bitstream.ReadFloat(b);
			m_MaxSpawnTime = Bitstream.ReadFloat(b);
		}

		class Path
		{
			public Vector3[] track;
			public int next;
			public float time;
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
	
			public State CurState;
			public uint PathCooldown;
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
				UnityMMO.Debug.Log("No poly at " + character.Position.x + " " + character.Position.z);
				// halp!
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
				if (ch.Data.HumanControllable && ch.Spawned)
				{
					int idx;
					float ground_y;
					if (!character.World._navMVP.GetPoly(character.Position, out idx, out ground_y))
					{
						continue;
					}

					float dist = _Dist(ch.Position, character.Position);
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
			float angle = (float)Math.Acos(Vector3.Dot(heading, new Vector3(toTargetNorm.x, 0, toTargetNorm.z)));

			float turnAmt = Math.Abs(angle);
			int turnDir = Math.Sign((float)Vector3.Cross(heading, toTargetNorm).y);
			if (turnDir == 0)
				turnDir = 1;

			float turn = dt * 3.1415f;
			if (turn > turnAmt)
				turn = turnAmt;

			character.Heading += turn * turnDir;

			while (character.Heading > tau)
				character.Heading -= tau;
			while (character.Heading < 0)
				character.Heading += tau;

			return Vector3.Dot(toTargetNorm, ServerCharacter.HeadingVector(character.Heading));
		}

		private bool FollowPath(ServerCharacter character, Data d, float dt, Path p)
		{
			Vector3 pathTarget = CurrentGoal(p);

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

			if (toTargetDsq > 0.00001f)
			{
				float toTargetD = (float)Math.Sqrt(toTargetDsq);
				Vector3 toTargetNorm = (1.0f / (toTargetD) * toTarget);

				float headingAmt = TurnTowards(character, d, toTargetNorm, dt);
				if (headingAmt < 0)
				{
					headingAmt = 0;
				}
				
				float move = headingAmt * dt * m_MoveSpeed;
				if (move > toTargetD)
				{
					move = toTargetD;
				}

				character.Position = character.Position + move * toTargetNorm;
				SnapToNavMesh(character, d);
			}

			float dx = pathTarget.x - character.Position.x;
			float dz = pathTarget.z - character.Position.z;
			float dsq = dx * dx + dz * dz; 

			if (_Dist(character.Position, p.track[p.track.Length-1]) < 0.01f)
			{
				return false;
			}

			return true;
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
			float dx, dy;

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

		public void ControlMe(uint iteration, ServerCharacter character)
		{
			if (!character.Spawned)
			{
				Debug.Log("Spawn: AI at " + character.Data.DefaultSpawnPos.x + ":" + character.Data.DefaultSpawnPos.z);
				character.Position = character.Data.DefaultSpawnPos;
				character.Velocity.x = 0;
				character.Velocity.y = 0;
				character.Velocity.z = 0;
				character.CharacterTypeId = m_Character;
				character.Spawned = true;
				character.TimeOffset = 0;
				character.GotNew = true;

				Data nd = new Data();
				nd.GroundedOnPoly = -1;
				nd.LastControllerUpdate = iteration;
				nd.CurState = Data.State.IDLE;
				character.ControllerData = nd;
			}

			if (character.ControllerData == null)
				return;

			Data d = character.ControllerData as Data;

			// -------------
			// Normal update

			float dt = 0.001f * (iteration - d.LastControllerUpdate);
			d.LastControllerUpdate = iteration;

			WorldServer w = character.World;

			int idx;
			float ground_y;
			float height_y = 2.00f;

			if (d.GroundedOnPoly == -1)
			{
				Fall(character, d, dt);
				character.GotNew = true;
			}

			if (d.GroundedOnPoly != -1)
			{
				switch (d.CurState)
				{
					case Data.State.IDLE:
						{
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
									float height;
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
								if (!FollowPath(character, d, dt, d.CurrentPath))
								{
									Console.WriteLine("Patrol done");
									d.CurrentPath = null;
									d.CurState = Data.State.IDLE;
								}
							}

							ServerCharacter potTarget = SpotTarget(character, d);
							if (potTarget != null)
							{
								Path p = PlanPathTo(character, potTarget.Position);
								if (p != null)
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
						break;

					case Data.State.CHASE:
						{
							if (d.CurrentPath == null || d.Target == null || !d.Target.Spawned)
							{
								Console.WriteLine("Target unspawned, lost target or it unspawned. Idling.");
								d.CurState = Data.State.IDLE;
								break;
							}

							if (!FollowPath(character, d, dt, d.CurrentPath))
							{
								Console.WriteLine("Path following done, I go idle.");
								d.CurState = Data.State.IDLE;
								break;
							}
	
							float dx = d.TargetPathTarget.x - d.Target.Position.x;
							float dz = d.TargetPathTarget.z - d.Target.Position.z;
							float mx = d.TargetPathTarget.x - character.Position.x;
							float mz = d.TargetPathTarget.z - character.Position.z;
							double dd = Math.Sqrt(dx * dx + dz * dz);
							double dm = Math.Sqrt(mx * mx + mz * mz);
							if (dd > dm * 0.20f && iteration > d.PathCooldown)
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

						if (_Dist(d.Target.Position, character.Position) < m_EngageDistance)
						{
							Console.WriteLine("Engaging in attack!");
							d.CurState = Data.State.ATTACK;
						}
						break;

					case Data.State.ATTACK:
						{
							if (!d.Target.Spawned)
							{
								Console.WriteLine("Target died, going idle");
								d.CurState = Data.State.IDLE;
								break;
							}

							float angle = 1.0f;
							Vector3 diff = d.Target.Position - character.Position;
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

							if (angle > 0.20f)
							{
								Console.WriteLine("I can hit, angle=" + angle);
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
