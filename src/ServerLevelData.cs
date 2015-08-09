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
		}
	
		public static void LoadIntoWorld(Bitstream.Buffer buf, WorldServer server, LoadServerCharacter loadCharacter)
		{
			int characters = Bitstream.ReadCompressedInt(buf);
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
		}
	}
}