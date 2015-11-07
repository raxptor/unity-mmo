using System.Collections.Generic;
using netki;

namespace UnityMMO
{
	public class ServerLevelData
	{
		public static void ParseCharacter(Bitstream.Buffer buf, ServerCharacterData data)
		{
			data.Id = Bitstream.ReadCompressedUint(buf);
			data.HumanControllable = Bitstream.ReadBits(buf, 1) != 0;
			data.DefaultSpawnPos.x = Bitstream.ReadFloat(buf);
			data.DefaultSpawnPos.y = Bitstream.ReadFloat(buf);
			data.DefaultSpawnPos.z = Bitstream.ReadFloat(buf);
		}

		public delegate byte[] LoadNavMeshFn(string pathFile);
	
		public static void LoadIntoWorld(Bitstream.Buffer buf, WorldServer server, LoadNavMeshFn navMeshFn)
		{
			while (true)
			{
				uint type = Bitstream.ReadCompressedUint(buf);
				if (type == 0 || buf.error != 0)
					break;

				outki.Item item = new outki.Item();
				item.Id = Bitstream.ReadCompressedUint(buf);
				item.DebugName = Bitstream.ReadStringDumb(buf);
				item.Equip = (outki.EquipType) Bitstream.ReadCompressedUint(buf);
				item.StackSize = Bitstream.ReadCompressedUint(buf);

				switch (type)
				{
					// generic
					case 1:
						{
						}
						break;
					case 2:
						{
							outki.WeaponInfo wi = new outki.WeaponInfo();
							wi.Type = (outki.WeaponType) Bitstream.ReadCompressedInt(buf);
							wi.Damage = Bitstream.ReadCompressedUint(buf); // damage
							wi.Range = Bitstream.ReadFloat(buf); // range
							wi.Capacity = Bitstream.ReadCompressedUint(buf);
							wi.AmmoType = (outki.AmmoType)Bitstream.ReadCompressedUint(buf);
							wi.StaminaCost = Bitstream.ReadFloat(buf);
							item.Weapon = wi;
							break;
						}
					case 3:
						{
							outki.AmmoInfo ai = new outki.AmmoInfo();
							ai.AmmoType = (outki.AmmoType) Bitstream.ReadCompressedUint(buf);
							item.Ammo = ai;
							break;
						}
					default:
						Debug.Log("Unknown item type " + type);
						break;
				}

				Debug.Log("Adding item id:" + item.Id + " debugname:" + item.DebugName);
				server.AddItemData(item);
			}

			int characters = Bitstream.ReadCompressedInt(buf);
		    Bitstream.ReadCompressedInt(buf);
			for (int i=0;i<characters;i++)
			{
				ServerCharacterData d = new ServerCharacterData();
				ServerLevelData.ParseCharacter(buf, d);
				ServerCharacter c = new ServerCharacter(d);

				// parse controller
				if (!c.Data.HumanControllable)
				{
					uint ctrl = Bitstream.ReadCompressedUint(buf);
					if (ctrl == 1)
					{
						ServerZombieAIController sz = new ServerZombieAIController();
						sz.Parse(buf);
						c.Controller = sz;
					}
				}

				server.AddCharacter(c);
			}
			int spawnpoints = Bitstream.ReadCompressedInt(buf);
			for (int j=0;j<spawnpoints;j++)
			{
				Vector3 pos;
				pos.x = Bitstream.ReadFloat(buf);
				pos.y = Bitstream.ReadFloat(buf);
				pos.z = Bitstream.ReadFloat(buf);
				server.AddSpawnpoint(pos);
			}

			uint sharedEntities = Bitstream.ReadCompressedUint(buf);
			Debug.Log("Loading " + sharedEntities + " shared entities");
			for (uint k = 0; k < sharedEntities; k++)
			{
				string type = Bitstream.ReadStringDumb(buf);
				uint id = Bitstream.ReadCompressedUint(buf);
				SetupSharedEntity(buf, server, type, id);
			}

			Bitstream.ReadStringDumb(buf);
			string pathFile = Bitstream.ReadStringDumb(buf);
			Debug.Log("Path file is [" + pathFile + "]");

			server.LoadNavMesh(navMeshFn(pathFile));
		}

		private static void SetupSharedEntity(Bitstream.Buffer buf, WorldServer server, string type, uint id)
		{
			Debug.Log("Entity is " + type + ":" + id);
			if (type == "scavenge")
			{
				ScavengeEntity se = new ScavengeEntity();
				se.m_Id = id;
				se.m_Position.x = Bitstream.ReadFloat(buf);
				se.m_Position.y = Bitstream.ReadFloat(buf);
				se.m_Position.z = Bitstream.ReadFloat(buf);
				se.m_MaxInteractionRadius = Bitstream.ReadFloat(buf);
				se.m_RespawnTimeMin = Bitstream.ReadFloat(buf);
				se.m_RespawnTimeMax = Bitstream.ReadFloat(buf);
				se.m_UseTime = Bitstream.ReadFloat(buf);
				server.AddEntity(se);
				Debug.Log("Adding scavenge at " + se.m_Position);
			}
		}

	}
}