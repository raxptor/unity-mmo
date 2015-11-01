using System.Collections.Generic;
using netki;

namespace UnityMMO
{
	public class ServerLevelData
	{
		public delegate ServerCharacter LoadServerCharacter(Bitstream.Buffer buf, string name);
		
		public static void ParseCharacter(Bitstream.Buffer buf, ServerCharacterData data)
		{
			data.Id = Bitstream.ReadCompressedUint(buf);
			data.HumanControllable = Bitstream.ReadBits(buf, 1) != 0;
			data.DefaultSpawnPos.x = Bitstream.ReadFloat(buf);
			data.DefaultSpawnPos.y = Bitstream.ReadFloat(buf);
			data.DefaultSpawnPos.z = Bitstream.ReadFloat(buf);
		}
	
		public static void LoadIntoWorld(Bitstream.Buffer buf, WorldServer server, LoadServerCharacter loadCharacter)
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
							Bitstream.ReadCompressedUint(buf); // damage
							Bitstream.ReadFloat(buf); // range
							item.Weapon = wi;
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
			int check = Bitstream.ReadCompressedInt(buf);
			for (int i=0;i<characters;i++)
			{
				ServerCharacter c = loadCharacter(buf, "serv" + i);
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

			string editorPrefix = Bitstream.ReadStringDumb(buf);
			string pathFile = Bitstream.ReadStringDumb(buf);
			Debug.Log("Path file is [" + pathFile + "]");

			server.LoadNavMesh(editorPrefix, pathFile);
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
				server.AddEntity(se);
				Debug.Log("Adding scavenge at " + se.m_Position);
			}
		}

	}
}