using System;
using netki;

namespace UnityMMO
{
	public class ServerZombieAIController : Controller
	{
		const float height_y = 2.0f;

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

			ground_y += height_y;

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
					if (dist < closest || closest == 0)
					{
						closest = dist;
						d.Target = ch;
						d.PathToTarget = null;
						Debug.Log("My target is " + ch.Data.Id + " away:" + dist);
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
				Debug.Log("Aborting hunt because target is gone!");
				return;
			}

			if (d.PathToTarget == null)
			{
				d.PathToTarget = character.World._navMVP.MakePath(character.Position, d.Target.Position);
				d.PathNext = 0;
			}
			else
			{	
				Debug.Log("I have a path which is " + d.PathToTarget.Length + " entries");

				if (d.PathNext < d.PathToTarget.Length)
				{
					Vector3 next = d.PathToTarget[d.PathNext];

					float dx = next.x - character.Position.x;
					float dy = next.y - character.Position.y;
					float dz = next.z - character.Position.z;
					float dsq = dx * dx + dy * dy + dz * dz; 
					if (dsq < 1.0f)
					{
						d.PathNext++;
						if (d.PathNext == d.PathToTarget.Length)
						{
							d.PathNext = 0;
							character.Position = d.PathToTarget[0];
						}
						return;
					}

					float spd = dt * 20.0f;
					float dinv = 1.0f / (float)Math.Sqrt(dsq);
					float amt = spd * dinv;
					character.Position.x += amt * dx; 
					character.Position.y += amt * dy; 
					character.Position.z += amt * dz; 
				}
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
				character.CharacterTypeId = "defaultplayer";
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
				
			character.GotNew = true;
		}
	}
}
