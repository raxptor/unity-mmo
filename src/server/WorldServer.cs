using System;
using System.Collections.Generic;
using netki;

namespace UnityMMO
{
	public class WorldObserver
	{
		public Vector3 FilterPosition;
		public bool[] CharacterFilter;
		public List<Bitstream.Buffer> UpdatesReliable = new List<Bitstream.Buffer>();
		public List<Bitstream.Buffer> UpdatesUnreliable = new List<Bitstream.Buffer>();
	}

	public interface ILevelQuery
	{
		/*
		bool IsValidLocation(Vector3 position);
		bool CanNavigate(Vector3 from, Vector3 to);
		Vector3[] Navigate(Vector3 from, Vector3 to);
		*/
	}

	public class WorldServer
	{
		public List<ServerCharacter> _activeCharacters;
		public List<WorldObserver> _observers = new List<WorldObserver>();
		public ILevelQuery _levelQueries;
		public outki.GameConfiguration _config;

		// ticks two times per update.
		uint _updateIteration;

		public WorldServer(ILevelQuery query, outki.GameConfiguration config)
		{
			_activeCharacters = new List<ServerCharacter>();
			_config = config;
		}

		public void AddCharacter(ServerCharacter ch)
		{
			lock (this)
			{
				_activeCharacters.Add(ch);
			}
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

		public void AddSpawnpoint(Vector3 pos)
		{
		
		}

		public void RemoveObserver(WorldObserver obs)
		{
			lock (this)
			{
				_observers.Remove(obs);
			}
		}

		public void StopControlling(ServerCharacter character)
		{
			lock (this)
			{
				character.Controller = null;
			}
		}

		public ServerCharacter GrabHumanControllable(Controller cont)
		{
			lock (this)
			{
				foreach (ServerCharacter ch in _activeCharacters)
				{
					if (ch.Controller == null && ch.Data.HumanControllable)
					{
						ch.Controller = cont;
						return ch;
					}
				}
			}
			return null;
		}

		public void Update(float dt)
		{
			DoGameUpdate(dt);
		}

		private void DoGameUpdate(float dt)
		{
			lock (this)
			{
				foreach (ServerCharacter sc in _activeCharacters)
				{
					if (sc.Controller != null)
						sc.Controller.ControlMe(sc);

					sc.Update(dt);
				}

				_updateIteration++;

				foreach (WorldObserver obs in _observers)
				{
					UpdateCharacterFilter(obs);
				}

				_updateIteration++;

				UpdateUnreliableAll();
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
						DatagramCoding.WriteUpdateBlockHeader(outp, UpdateBlock.Type.FILTER);
						Bitstream.PutBits(outp, 24, _updateIteration);
					}

					Bitstream.PutBits(outp, 15, (uint)i);
					Bitstream.PutBits(outp, 1, (uint)(target ? 1 : 0));

					if (target)
					{
						_activeCharacters[i].WriteFullState(outp);
					}
				}
			}

			if (outp != null)
			{
				outp.Flip();
				obs.UpdatesReliable.Add(outp);
			}
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
					next.Flip();
					outs[i] = next;
					next = null;
				}
			}

			foreach (WorldObserver obs in _observers)
			{
				Bitstream.Buffer output = null;
				for (int i = 0; i < _activeCharacters.Count; i++)
				{
					if (outs[i] != null && obs.CharacterFilter[i])
					{
						if (output == null)
						{
							output = Bitstream.Buffer.Make(new byte[512]);
							DatagramCoding.WriteUpdateBlockHeader(output, UpdateBlock.Type.CHARACTERS);
							Bitstream.PutBits(output, 24, _updateIteration);
						}
						// character index
						Bitstream.PutBits(output, 16, (uint)i);
						Bitstream.Insert(output, outs[i]);
					}
				}

				if (output != null)
				{
					output.Flip();
					obs.UpdatesUnreliable.Add(output);
				}
			}
		}
	}
}
