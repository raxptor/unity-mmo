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
			public State CurState;
			public int PathNext;
			public uint PathCooldown;
		};

		public const uint PathCooldownIterations = 500;

		public static float _Dist(Vector3 a, Vector3 b)
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
			if (d.PathNext < path.Length)
			{
				Vector3 next = path[d.PathNext];

				float dx = next.x - character.Position.x;
				float dz = next.z - character.Position.z;
				float dsq = dx * dx + dz * dz; 

				float nextHeading = (float)-Math.Atan2(-dx, dz);

				const float tau = 6.28f;
				float a = Math.Abs(character.Heading - nextHeading);
				if (Math.Abs(character.Heading + tau - nextHeading) < a)
					character.Heading += tau;
				else if (Math.Abs(character.Heading - tau - nextHeading) < a)
					character.Heading -= tau;

				character.Heading = character.Heading + 0.90f * (nextHeading - character.Heading);

				if (dsq < 0.001f)
				{
					d.PathNext++;
					if (d.PathNext == path.Length)
					{
						d.PathNext = 0;
						character.Position = path[path.Length-1];
						return false;
					}
					return true;
				}

				float spd = dt * m_MoveSpeed;
				float dinv = 1.0f / (float)Math.Sqrt(dsq);
				float amt = spd * dinv;
				if (amt > 1)
				{
					amt = 1.0f;
				}
			
				Vector3 test = new Vector3(character.Position.x + amt * dx, character.Position.y, character.Position.z + amt * dz);

				float height;
				int poly;
				if (character.World._navMVP.GetPoly(test, out poly, out height))
				{
					test.y = height;
					// correct for speed here.
				}
				else
				{
					test = next;
					return false;
				}

				character.Position = test;

				return true;
			}
			else
			{
				return false;
			}
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

								if (_Dist(patrol_pos, character.Position) > 2.0f)
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
											d.PathNext = 1;
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
									Console.WriteLine("I can chase target! " + d.PathToTarget.Length + " nodes path.");
									d.PathNext = 1;
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
							float dx = last.x - d.Target.Position.x;
							float dz = last.z - d.Target.Position.z;
							if (dx * dx + dz * dz > 1.0f && iteration > d.PathCooldown)
							{
								d.PathCooldown = iteration + PathCooldownIterations;
								Vector3[] potNew = MakeClosePath(character, character.Position, d.Target.Position);
								if (potNew != null)
								{
									d.PathToTarget = potNew;
									d.PathNext = 1;
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
