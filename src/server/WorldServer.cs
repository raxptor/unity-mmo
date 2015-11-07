using System;
using System.Collections.Generic;
using netki;

namespace UnityMMO
{
	public class WorldObserver
	{
		public ServerCharacter TrackCharacter;
		public Vector3 FilterPosition;
		public float FilterRange;
		public bool[] CharacterFilter;
		public List<Bitstream.Buffer> UpdatesReliable = new List<Bitstream.Buffer>();
		public List<Bitstream.Buffer> UpdatesUnreliable = new List<Bitstream.Buffer>();
	}

	public interface ILevelQuery
	{
		// bool IsValidLocation(Vector3 position);
		// bool CanNavigate(Vector3 from, Vector3 to);
		// Vector3[] Navigate(Vector3 from, Vector3 to);
	}

	public class WorldServer
	{
		public List<Entity> _activeEntities = new List<Entity>();
		public List<ServerCharacter> _activeCharacters;
		public List<WorldObserver> _observers = new List<WorldObserver>();
		public ILevelQuery _levelQueries;
		public NavMeshMVP _navMVP;

		public outki.GameConfiguration _config;
		public List<Vector3> _humanSpawnPoints = new List<Vector3>();
		public Dictionary<uint, outki.Item> m_itemData = new Dictionary<uint, outki.Item>();

		DateTime _startTime = DateTime.Now;

		public uint _timeScale = 1;
		private uint _itemInstanceIDs = 1234;

		public bool _characterMirroringHax = false;

		public struct LoadoutEntry
		{
			public uint ItemTypeId;
			public uint Count;
			public uint Slot;
			public uint State;
			public uint EquipOnCharacter;
		}

		public LoadoutEntry[] m_startingInventory;


		public WorldServer(ILevelQuery query, outki.GameConfiguration config)
		{
			_activeCharacters = new List<ServerCharacter>();
			_config = config;
		}

		public ServerPlayer.ItemInstance MakeItem(outki.Item item, uint count, uint slot)
		{
			ServerPlayer.ItemInstance inst = new ServerPlayer.ItemInstance();
			inst.Item = item;
			inst.Id = ++_itemInstanceIDs;
			inst.Count = count;
			inst.Slot = slot;
			return inst;
		}

		public ServerPlayer MakeNewPlayer(string name, uint id)
		{
			ServerPlayer p = new ServerPlayer(name, id);

			foreach (var se in m_startingInventory)
			{
				ServerPlayer.ItemInstance neue = MakeItem(m_itemData[se.ItemTypeId], se.Count, se.Slot);
				neue.UserState = se.State;
				neue.EquippedInSlot = se.EquipOnCharacter;
				p.Inventory.Add(neue);
			}
				
			Console.WriteLine("Gave out " + p.Inventory.Count + " items");
			return p;
		}

		public void AddItemData(outki.Item item)
		{
			if (m_itemData.ContainsKey(item.Id))
			{
				Debug.Log("Failed to add item [" + item.DebugName + "] because id already exists");
			}
			else
			{
				m_itemData.Add(item.Id, item);
			}
		}

		public void AddEntity(Entity e)
		{
			lock (this)
			{
				_activeEntities.Add(e);
			}
		}

		public void AddCharacter(ServerCharacter ch)
		{
			lock (this)
			{
				ch.World = this;
				ch.m_Id = (uint)_activeCharacters.Count;
				_activeCharacters.Add(ch);
			}
		}

		public void SyncNonCharacters(uint iteration, WorldObserver observer)
		{
			// Sync mandatory state
			Bitstream.Buffer outp = Bitstream.Buffer.Make(new byte[4096]);
			DatagramCoding.WriteUpdateBlockHeader(outp, UpdateBlock.Type.ENTITIES);
			Bitstream.PutCompressedUint(outp, iteration); // hax
			for (int i = 0; i < _activeEntities.Count; i++)
			{
				Bitstream.PutCompressedUint(outp, _activeEntities[i].m_Id);
				Bitstream.SyncByte(outp);
				_activeEntities[i].WriteFullState(outp);
				Bitstream.SyncByte(outp);
				if (outp.error != 0)
				{
					Console.WriteLine("Entity sync buffer overflow!");
				}
			}
			outp.Flip();
			Console.WriteLine("Sending observer entity update block of " + outp.bufsize + " bytes");
			observer.UpdatesReliable.Add(outp);
		}

		public WorldObserver AddObserver(ServerCharacter tracking)
		{
			WorldObserver ws = new WorldObserver();
			ws.TrackCharacter = tracking;
			ws.CharacterFilter = new bool[_activeCharacters.Count];
			ws.FilterRange = 100.0f;

			lock (this)
			{
				_observers.Add(ws);
				SyncNonCharacters(0, ws);
			}
			return ws;
		}

		public void ResetCharacter(ServerPlayer p, ServerCharacter c)
		{
			c.ResetFromData(c.Data);
			c.Equipped.Clear();
			foreach (var pi in p.Inventory)
			{
				if (pi.EquippedInSlot != 0)
					ServerPlayerCommands.HandlePlayerEquip(p, c, 1, pi.Id);
			}
			c.SendNewEquip = true;
		}

		public void AddSpawnpoint(Vector3 pos)
		{
			_humanSpawnPoints.Add(pos);
		}
	
		public bool GetPointForHumanSpawn(out Vector3 pos)
		{
			pos = new Vector3(1, 2, 3);
			if (_humanSpawnPoints.Count == 0)
				return false;
			pos = _humanSpawnPoints[0];
			return true;
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
			uint iteration = _timeScale * (uint)((DateTime.Now - _startTime).Ticks * 1000 / TimeSpan.TicksPerSecond);
			lock (this)
			{
				foreach (Entity entity in _activeEntities)
				{
					entity.Update(iteration, dt);
				}

				foreach (ServerCharacter sc in _activeCharacters)
				{
					if (sc.Controller != null)
						sc.Controller.ControlMe(iteration, sc);

					sc.Update(iteration, dt);
				}

				foreach (WorldObserver obs in _observers)
				{
					if (obs.TrackCharacter != null)
						obs.FilterPosition = obs.TrackCharacter.Position;
					UpdateCharacterFilter(iteration, obs);
				}

				if (_characterMirroringHax)
				{
					_activeCharacters[1].MirrorIt(_activeCharacters[0], new Vector3(0,0,-4));
					_activeCharacters[2].MirrorIt(_activeCharacters[0], new Vector3(0,0,4));
					_activeCharacters[3].MirrorIt(_activeCharacters[0], new Vector3(4,0,0));
					_activeCharacters[4].MirrorIt(_activeCharacters[0], new Vector3(-4,0,0));
				}

				UpdateUnreliableAll(iteration);
				UpdateReliableAll(iteration);
			}
		}

		// Characters in view.
		private void UpdateCharacterFilter(uint iteration, WorldObserver obs)
		{
			// Reliable state update
			//   1. Character enters filter, send whole enter state.
			//   2. Character exits filter, send whole disappear state.
			Bitstream.Buffer outp = null;
			for (int i = 0; i < _activeCharacters.Count; i++)
			{
				bool target = false;

				if (_activeCharacters[i].Spawned)
				{
					// use what we had and see if it needs to change
					// depending on distance
					target = obs.CharacterFilter[i];

					Vector3 diff = obs.FilterPosition - _activeCharacters[i].Position;
					float distSq = Vector3.Dot(diff, diff);
					// is visible but too far away (some hysteresis here) 
					if (obs.CharacterFilter[i] && distSq > obs.FilterRange * obs.FilterRange * 1.15f)
					{
						Console.WriteLine(i + " Going out of filter range " + obs.FilterPosition + " dist=" + Math.Sqrt(distSq));
						target = false;
					}
					if (!obs.CharacterFilter[i] && distSq < obs.FilterRange * obs.FilterRange)
					{
						Console.WriteLine("Entering filter range");
						target = true;
					}
				}

				if (obs.CharacterFilter[i] != target)
				{	
					Console.WriteLine(i + " " + _activeCharacters[i].Spawned + " " + obs.CharacterFilter[i] + " => " + target);
					obs.CharacterFilter[i] = target;

					if (outp == null)
					{
						outp = Bitstream.Buffer.Make(new byte[1024]);
						DatagramCoding.WriteUpdateBlockHeader(outp, UpdateBlock.Type.FILTER);
						Bitstream.PutCompressedUint(outp, iteration);
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

		public void LoadNavMesh(byte[] data)
		{
			NavMeshMVP nm = new NavMeshMVP();
			nm.LoadFromBytes(data);
			_levelQueries = nm;
			_navMVP = nm;
		}
			
		// Characters in view.
		private void UpdateUnreliableAll(uint iteration)
		{
			// Unreliable state udptae
			//   1. All characters write (maybe) unreliable updates 
			Bitstream.Buffer[] outs = new Bitstream.Buffer[_activeCharacters.Count];
			Bitstream.Buffer next = null;
			for (int i = 0; i < _activeCharacters.Count; i++)
			{
				if (next == null)
				{
					next = Bitstream.Buffer.Make(new byte[128]);
				}
				if (_activeCharacters[i].WriteUnreliableUpdate(next))
				{
					Bitstream.SyncByte(next);
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
							output = Bitstream.Buffer.Make(new byte[4096]);
							DatagramCoding.WriteUpdateBlockHeader(output, UpdateBlock.Type.CHARACTERS);
							Bitstream.PutCompressedUint(output, iteration);
						}
						// character index
						Bitstream.PutBits(output, 16, (uint)i);
						Bitstream.SyncByte(output);
						Bitstream.Insert(output, outs[i]);
						Bitstream.SyncByte(output);
					}
				}

				if (output != null)
				{
					output.Flip();
					obs.UpdatesUnreliable.Add(output);
				}
			}
		}

		private void UpdateReliableAll(uint iteration)
		{
			Bitstream.Buffer[] outs = new Bitstream.Buffer[_activeCharacters.Count];
			Bitstream.Buffer[] entityOuts = new Bitstream.Buffer[_activeEntities.Count];

			Bitstream.Buffer next = null;
			for (int i = 0; i < _activeCharacters.Count; i++)
			{
				if (next == null)
				{
					next = Bitstream.Buffer.Make(new byte[128]);
				}
				if (_activeCharacters[i].WriteReliableUpdate(next))
				{
					Bitstream.SyncByte(next);
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
							Bitstream.PutCompressedUint(output, iteration);
						}
						// character index
						Bitstream.PutBits(output, 16, (uint)i);
						Bitstream.SyncByte(output);
						Bitstream.Insert(output, outs[i]);
						Bitstream.SyncByte(output);
					}
				}

				if (output != null)
				{
					output.Flip();
					obs.UpdatesReliable.Add(output);
				}
			}
				
			for (int i=0;i<_activeEntities.Count;i++)
			{
				if (next == null)
				{
					next = Bitstream.Buffer.Make(new byte[128]);
				}
				if (_activeEntities[i].WriteReliableUpdate(next))
				{
					Bitstream.SyncByte(next);
					next.Flip();
					entityOuts[i] = next;
					next = null;
				}
			}
				
			foreach (WorldObserver obs in _observers)
			{
				Bitstream.Buffer output = null;
				for (int i = 0; i < _activeEntities.Count; i++)
				{
					if (entityOuts[i] != null)
					{
						if (output == null)
						{
							output = Bitstream.Buffer.Make(new byte[1024]);
							DatagramCoding.WriteUpdateBlockHeader(output, UpdateBlock.Type.ENTITIES);
							Bitstream.PutCompressedUint(output, iteration);
						}
						Bitstream.PutCompressedUint(output, _activeEntities[i].m_Id);
						Bitstream.SyncByte(output);
						Bitstream.Insert(output, entityOuts[i]);
						Bitstream.SyncByte(output);
					}
				}

				if (output != null)
				{
					output.Flip();
					obs.UpdatesReliable.Add(output);
				}
			}
		}
	}
}
