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
			public uint LastControllerUpdate;
			public ServerCharacter Target;
			public Vector3[] PathToTarget;
			public Vector3[] PathToPatrol;
			public bool TargetPathIsComplete;
			public Vector3 TargetPathTarget;
			public State CurState;
			public int PathNext;
			public float PathT;
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
			d.y = b.y - a.y;
			d.z = b.z - a.z;
			return (float) Math.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z);
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
					UnityMMO.Debug.Log("Fell to point y=" + ground_y);
				}
			}
		}

		private ServerCharacter FindTarget(ServerCharacter character, Data d)
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

		private bool FollowPath(ServerCharacter character, Data d, float dt, Vector3[] path)
		{
			float left = 0.0f;
			Vector3 pathTarget = character.Position;
				
			int p0 = d.PathNext;
			int p1 = d.PathNext + 1;
			if (p1 < path.Length)
			{
				float dist = _Dist(path[p0], path[p1]);
				float rate = m_MoveSpeed / dist;
				float inc = dt * rate;

				d.PathT += inc;
				if (d.PathT > 1.0f)
				{
					left = ((d.PathT - 1.0f) * dist) / m_MoveSpeed;
					d.PathT = 0.0f;
					d.PathNext++;
					pathTarget = path[p1];
				}
				else
				{
					pathTarget = path[p0] + d.PathT * (path[p1] - path[p0]);
				}
			}
			else
			{
				pathTarget = path[path.Length - 1];
			}

			character.Position.x += 0.50f * (pathTarget.x - character.Position.x);
			character.Position.y += 0.50f * (pathTarget.y - character.Position.y);
			character.Position.z += 0.50f * (pathTarget.z - character.Position.z);

			float dx = pathTarget.x - character.Position.x;
			float dz = pathTarget.z - character.Position.z;
			float dsq = dx * dx + dz * dz; 

			character.Heading = (float)-Math.Atan2(-dx, dz);

			if (_Dist(character.Position, pathTarget) < 0.001f && p1 >= path.Length)
			{
				return false;
			}

			if (left > 0.0f)
			{
				return FollowPath(character, d, left, path);
			}

			return true;
		}

		public Vector3[] MakeClosePath(ServerCharacter character, Vector3 begin, Vector3 end)
		{
			NavMeshMVP nav = character.World._navMVP;
			int idx;
			float y;
			float dx, dy;

			// TODO: Code approximator make path instead.
			Random r = new Random();
			Vector3 tryend = end;
			tryend.y += 0.20f;
			for (int i = 0; i < 10; i++)
			{
				if (nav.GetPoly(tryend, out idx, out y))
					return nav.MakePath(begin, tryend, null);

				tryend.x = end.x + (float)(r.NextDouble() - 0.5f) * 0.25f;
				tryend.z = end.z + (float)(r.NextDouble() - 0.5f) * 0.25f;
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
				ServerCharacter tgt = FindTarget(character, d);
				if (d.CurState == Data.State.IDLE && tgt != null)
					d.CurState = Data.State.PATROL;

				switch (d.CurState)
				{
					case Data.State.IDLE:
						{
							if (iteration > d.PathCooldown)
							{
								// Make patrol.
								Vector3 patrol_pos = character.Data.DefaultSpawnPos;
								patrol_pos.x += (float)(2.0f * m_PatrolRadius * (m_random.NextDouble() - 0.5f));
								patrol_pos.z += (float)(2.0f * m_PatrolRadius * (m_random.NextDouble() - 0.5f));

								if (_Dist2D(patrol_pos, character.Position) > 2.0f)
								{
									NavMeshMVP nav = character.World._navMVP;
									int poly;
									float height;
									if (nav.GetPoly(patrol_pos, out poly, out height))
									{
										patrol_pos.y = height;
										d.PathToPatrol = nav.MakePath(character.Position, patrol_pos, null);
										if (d.PathToPatrol != null)
										{
											Console.WriteLine("Patrol from " + character.Position + " to " + patrol_pos);
											d.PathNext = 0;
											d.PathT = 0;
											d.CurState = Data.State.PATROL;
										}
									}
								}
								d.PathCooldown = iteration + PathCooldownIterations;
							}
						}
						break;

					case Data.State.PATROL:
						{
							//
							if (tgt != null && iteration > d.PathCooldown)
							{
								d.PathCooldown = iteration + PathCooldownIterations;
								d.PathToTarget = MakeClosePath(character, character.Position, tgt.Position);
								if (d.PathToTarget != null)
								{
									d.TargetPathTarget = tgt.Position;
									d.TargetPathIsComplete = d.PathToTarget[d.PathToTarget.Length - 1] == tgt.Position;
									d.PathNext = 0;
									d.PathT = 0;
									d.Target = tgt;
									d.CurState = Data.State.CHASE;
									break;
								}
							}

							if (d.PathToPatrol != null)
							{						
								if (!FollowPath(character, d, dt, d.PathToPatrol))
								{
									Console.WriteLine("Done patroling, going idle.");
									d.CurState = Data.State.IDLE;
								}
							}
						}
						break;

					case Data.State.CHASE:
						{
							if (!d.Target.Spawned)
							{
								Console.WriteLine("Target unspawned, going idle.");
								d.CurState = Data.State.IDLE;
							}

							Vector3 last = d.PathToTarget[d.PathToTarget.Length - 1];
							// target to character
							float dx = d.TargetPathTarget.x - d.Target.Position.x;
							float dz = d.TargetPathTarget.z - d.Target.Position.z;
							// me to character
							float mx = last.x - character.Position.x;
							float mz = last.z - character.Position.z;

							double dd = Math.Sqrt(dx*dx + dz*dz);
							double dm = Math.Sqrt(mx * mx + mz * mz);
							if (dd > dm * 0.20f && iteration > d.PathCooldown)
							{
								d.PathCooldown = iteration + PathCooldownIterations;
								Vector3[] potNew = MakeClosePath(character, character.Position, d.Target.Position);
								if (potNew != null)
								{
									d.PathToTarget = potNew;
									d.PathNext = 0;
									d.PathT = 0;
									d.TargetPathTarget = tgt.Position;
									d.TargetPathIsComplete = d.PathToTarget[d.PathToTarget.Length - 1] == tgt.Position;
								}

								if (dx * dx + dz * dz > 250.0f)
								{
									Debug.Log("Target ran away super far");
									d.CurState = Data.State.IDLE;
								}
							}

							if (!FollowPath(character, d, dt, d.PathToTarget))
							{
								d.CurState = Data.State.IDLE;
								break;
							}
						}
						break;
				}
			}

				
			character.GotNew = true;
		}
	}
}
