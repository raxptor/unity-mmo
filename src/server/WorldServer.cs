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
		public List<Bitstream.Buffer> UpdatesReliable = new List<Bitstream.Buffer>();
		public List<Bitstream.Buffer> UpdatesUnreliable = new List<Bitstream.Buffer>();
	}

    public class WorldServer
    {
		List<ServerCharacter> _activeCharacters;
		List<WorldObserver> _observers = new List<WorldObserver>();
		float _timeAccum, _tickTime;
		uint _updateIteration;

	
        public WorldServer(LevelData data)
        {
			_tickTime = 0.020f;
			_activeCharacters = new List<ServerCharacter>();
            foreach (ServerCharacterData sc in data.Characters)
				_activeCharacters.Add(new ServerCharacter(sc));
        }

		public WorldObserver AddObserver()
		{
			WorldObserver ws = new WorldObserver();
			ws.CharacterFilter = new bool[_activeCharacters.Count];

			lock (this)
			{
				_observers.Add(ws);
			}
			return ws;
		}

		public void RemoveObserver(WorldObserver obs)
		{
			lock (this)
			{
				_observers.Remove(obs);
			}
		}

		public void Update(float dt)
		{
			DoGameUpdate(dt);
		}

		private void DoGameUpdate(float dt)
		{
			lock (this)
			{
				foreach (ServerCharacter in _activeCharacters)
				{
					_activeCharacters.Update(dt);
				}

				foreach (WorldObserver obs in _observers)
				{
					UpdateCharacterFilter(obs);
					UpdateUnreliable(obs);
				}
			}
		}

		// Characters in view.
		private void UpdateCharacterFilter(WorldObserver obs)
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
						Bitstream.PutBits(outp, 24, _updateIteration);
					}

					Bitstream.PutBits(outp, 15, (uint)i);
					Bitstream.PutBits(outp, 1, (uint)target);

					if (target)
					{
						_activeCharacters[i].WriteFullState(outp);
					}
				}
			}

			if (outp != null)
				obs.UpdatesReliable.Add(outp);
		}

		// Characters in view.
		private void UpdateUnreliableAll()
		{
			// Unreliable state udptae
			//   1. All characters write (maybe) unreliable updates 
			Bitstream.Buffer[] outs = new Bitstream.Buffer[_activeCharacters.Count];
			Bitstream.Buffer next = null;
			for (int i = 0; i < _activeCharacters.Count; i++)
			{
				if (next == null)
				{
					next = Bitstream.Buffer.Make(new byte[64]);
				}
				if (_activeCharacters[i].WriteUnreliableUpdate(next))
				{
					outs[i] = next;
					next = null;
				}
			}

			foreach (WorldObserver obs in _observers)
			{
				Bitstream.Buffer output = null;
				for (int i=0;i<_activeCharacters.Count;i++)
				{
					if (obs.CharacterFilter[i])
					{
						if (output == null)
						{
							output = Bitstream.Buffer.Make(new byte[512]);
							UpdateMangling.BlockHeader(output, UpdateMangling.UPDATE_CHARACTERS);
							Bitstream.PutBits(output, 24, _updateIteration);
						}
						// character index
						Bitstream.PutBits(output, 16, i);
						Bitstream.Insert(output, outs[i]);
					}
				}

				if (output != null)
				{
					obs.UpdatesUnreliable.Add(output);
				}
			}
		}
    }
}
