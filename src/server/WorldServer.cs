using System;
using System.Collections.Generic;
using netki;

namespace UnityMMO
{
    public class LevelData
    {
        public List<ServerCharacterData> Characters; 
    }

	public class WorldObserver
	{
		public Vector3 FilterPosition;
		public bool[] CharacterFilter;
		public List<Bitstream.Buffer> UpdatesOrdered = new List<Bitstream.Buffer>();
		public List<Bitstream.Buffer> UpdatesUnOrdered = new List<Bitstream.Buffer>();
	}

    public class WorldServer
    {
		List<ServerCharacter> _activeCharacters;
	
        public WorldServer(LevelData data)
        {
			_activeCharacters = new List<ServerCharacter>();
            foreach (ServerCharacterData sc in data.Characters)
				_activeCharacters.Add(new ServerCharacter(sc));
        }

		public WorldObserver AddObserver()
		{
			WorldObserver ws = new WorldObserver();
			ws.CharacterFilter = new bool[_activeCharacters.Count];
			UpdateCharacterFilter(ws);
			return ws;
		}

		// Characters in view.
		public void UpdateCharacterFilter(WorldObserver obs)
		{
			// Reliable state update
			//   1. Character enters filter, send whole enter state.
			//   2. Character exits filter, send whole disappear state.
			Bitstream.Buffer outp = null;
			for (int i = 0; i < _activeCharacters.Count; i++)
			{
				bool target = _activeCharacters[i].Spawned;
				if (obs.CharacterFilter[i] != target)
				{
					obs.CharacterFilter[i] = target;

					if (outp == null)
					{
						outp = Bitstream.Buffer.Make(new byte[1024]);
						UpdateMangling.BlockHeader(outp, UpdateMangling.UPDATE_FILTER);
					}

					Bitstream.PutBits(outp, 15, (uint)i);
					Bitstream.PutBits(outp, 1, (uint)target);

					if (target)
					{
						_activeCharacters[i].WriteFullState(outp);
					}
				}
			}
		}

		public void PutCharacterStateBlock(netki.Bitstream.Buffer buf, ServerCharacter character)
		{

		}
    }
}
