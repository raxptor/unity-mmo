using System.Collections.Generic;
using netki;

namespace UnityMMO
{
	public class ServerLevelData
	{
		public delegate ServerCharacter LoadServerCharacter(Bitstream.Buffer buf, string name);
		
		public static void ParseCharacter(Bitstream.Buffer buf, ServerCharacterData data)
		{
			data.HumanControllable = Bitstream.ReadBits(buf, 1) != 0;
			
			Vector3 pos, rot;
			pos.x = Bitstream.ReadFloat(buf);
			pos.y = Bitstream.ReadFloat(buf);
			pos.z = Bitstream.ReadFloat(buf);
			rot.x = Bitstream.ReadFloat(buf);
			rot.y = Bitstream.ReadFloat(buf);
			rot.z = Bitstream.ReadFloat(buf);
			Bitstream.ReadFloat(buf);
			
			data.StartPosition = pos;
		}
	
		public static void LoadIntoWorld(Bitstream.Buffer buf, WorldServer server, LoadServerCharacter loadCharacter)
		{
			int characters = Bitstream.ReadCompressedInt(buf);
			for (int i=0;i<characters;i++)
			{
				int namelen = Bitstream.ReadCompressedInt(buf);
				byte[] bytes = Bitstream.ReadBytes(buf, namelen);
				ServerCharacter c = loadCharacter(buf, System.Text.UTF8Encoding.UTF8.GetString(bytes));
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
		}
	}
}