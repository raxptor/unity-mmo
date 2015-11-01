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
			public Data()
			{
				GroundedOnPoly = -1;
			}

			public int GroundedOnPoly;
			public uint LastControllerUpdate;
			public ServerCharacter Target;
			public Vector3[] PathToTarget;
			public Vector3[] PathToPatrol;
			public int PathNext;
		};

		public static float _Dist(Vector3 a, Vector3 b)
		{
			Vector3 d;
			d.x = b.x - a.x;
			d.y = b.y - a.y;
			d.z = b.z - a.z;
			return d.x * d.x + d.y * d.y + d.z * d.z;
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

		private void FindTarget(ServerCharacter character, Data d)
		{
			WorldServer w = character.World;
			foreach (ServerCharacter ch in w._activeCharacters)
			{
				float closest = 0;
				if (ch.Data.HumanControllable && ch.Spawned)
				{
					float dist = _Dist(ch.Position, character.Position);
					if (dist < closest || closest == 0 && dist < m_SearchRadius)
					{
						closest = dist;
						d.Target = ch;
						d.PathToTarget = null;
						Debug.Log("My target is " + ch.Data.Id + " away:" + dist + " position:" + ch.Position);
					}
				}
			}
		}

		private static Random r = new Random();

		private void HuntTarget(ServerCharacter character, Data d, float dt)
		{
			if (!d.Target.Spawned)
			{
				d.Target = null;
				d.PathToTarget = null;
			}

			if (d.PathToTarget == null)
			{
				d.PathToTarget = character.World._navMVP.MakePath(character.Position, d.Target.Position);
				d.PathNext = 0;
			}
				
			if (!FollowPath(character, d, dt, d.PathToTarget))
			{
				d.PathToTarget = null;
				d.Target = null;
			}
		}

		private void DoPatrol(ServerCharacter character, Data d, float dt)
		{
			if (!FollowPath(character, d, dt, d.PathToPatrol))
			{
				d.PathToPatrol = null;
			}
		}

		private bool FollowPath(ServerCharacter character, Data d, float dt, Vector3[] path)
		{
			if (d.PathNext < path.Length)
			{
				Vector3 next = path[d.PathNext];

				float dx = next.x - character.Position.x;
				float dy = next.y - character.Position.y;
				float dz = next.z - character.Position.z;
				float dsq = dx * dx + dy * dy + dz * dz; 

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
				character.Position.x += amt * dx; 
				character.Position.y += amt * dy; 
				character.Position.z += amt * dz; 
				return true;
			}
			else
			{
				return false;
			}
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
			}
			else if (d.Target == null)
			{
				FindTarget(character, d);
			}
			else
			{
				HuntTarget(character, d, dt);
			}

			// totally idle
			if (d.GroundedOnPoly != -1 && d.Target == null && d.PathToTarget == null && d.PathToPatrol == null)
			{					
				Vector3 patrol_pos = character.Data.DefaultSpawnPos;
				patrol_pos.x += (float)(2.0f * m_PatrolRadius * (m_random.NextDouble() - 0.5f));
				patrol_pos.z += (float)(2.0f * m_PatrolRadius * (m_random.NextDouble() - 0.5f));

				NavMeshMVP nav = character.World._navMVP;
				int poly; float height;
				if (nav.GetPoly(patrol_pos, out poly, out height))
				{
					patrol_pos.y = height;
					d.PathToPatrol = nav.MakePath(character.Position, patrol_pos);
					d.PathNext = 0;
					Console.WriteLine("Made patrol path to " + patrol_pos);
				}
			}

			if (d.GroundedOnPoly != -1 && d.Target == null && d.PathToPatrol != null)
			{
				DoPatrol(character, d, dt);
			}
				
			character.GotNew = true;
		}
	}
}
