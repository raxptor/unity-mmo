using System;
using System.Collections.Generic;

namespace UnityMMO
{
    public class LevelData
    {
        public List<ServerCharacterData> Characters; 
    }

    public class WorldServer
    {
        Dictionary<uint, ServerCharacter> _characters;

        public WorldServer(LevelData data)
        {
            _characters = new Dictionary<uint, ServerCharacter>();
            foreach (ServerCharacterData sc in data.Characters)
                _characters.Add(sc.Id, new ServerCharacter(sc));
        }
    }
}
