using Cube;
using netki;
using System.Collections.Generic;
using System;

namespace UnityMMO
{
	public class WorldClient
	{
		public interface Character
		{
			void OnEventBlock(uint iteration, Bitstream.Buffer block);
			void OnUpdateBlock(uint iteration, Bitstream.Buffer block);
			void OnFullStateBlock(uint iteration, Bitstream.Buffer block);
			void OnFilterChange(bool filtered);
		}

		public interface Entity
		{
			void OnUpdateBlock(uint iteration, Bitstream.Buffer block);
		}

		public class ItemInstance
		{
			public uint Id; // instance id
			public uint TypeId; // type id
			public object ItemData;
			public uint Count;
			public uint ChildCount;
			public uint UserState;
			public uint Slot;
			public List<ItemInstance> Children;
		};

		public class Player
		{
			public uint Id;
			public string Name;
			public bool IsSelf;
			public List<ItemInstance> Inventory;
		};

		private IGameInstClient _client;
		private Dictionary<int, Character> _characters;
		private Dictionary<uint, Entity> _entities;
		private Dictionary<uint, ItemInstance> _itemInstances;
		private List<Player> _players;
		private PacketLaneReliableOrdered _pl_reliable;
		private PacketLaneUnreliableOrdered _pl_unreliable;
		private Character _controlled;
		private Player _self;

		public List<Bitstream.Buffer> _player_events = new List<Bitstream.Buffer>();

		// last server timestamp
		private DateTime _startTime = DateTime.Now;
		private uint _startIteration = 0;

		public delegate object ResolveItemData(uint typeId);
		private ResolveItemData _itemDataResolver;

		public WorldClient(IGameInstClient client)
		{
			_client = client;
			_characters = new Dictionary<int, Character>();
			_entities = new Dictionary<uint, Entity>();
			_pl_reliable = new PacketLaneReliableOrdered();
			_pl_unreliable = new PacketLaneUnreliableOrdered();
			_players = new List<Player>();
			_itemInstances = new Dictionary<uint, ItemInstance>();
		}

		public void SetItemDataResolver(ResolveItemData rid)
		{
			_itemDataResolver = rid;
		}

		public ItemInstance GetItemInstance(uint instanceId)
		{
			ItemInstance itm;
			if (_itemInstances.TryGetValue(instanceId, out itm))
				return itm;
			return null;
		}

		public void AddCharacter(int index, Character character)
		{
			_characters.Add(index, character);
		}

		public void AddEntity(uint id, Entity entity)
		{
			_entities.Add(id, entity);
		}

		public Character GetControlledCharacter()
		{
			return _controlled;
		}

		private void OnUpdateFilterBlock(Bitstream.Buffer b)
		{
			uint iteration = Bitstream.ReadCompressedUint(b);

			while (true)
			{
				uint character = Bitstream.ReadBits(b, 15);
				uint enabled = Bitstream.ReadBits(b, 1);
				Debug.Log("char=" + character + " enabled=" + enabled); 

				if (b.error != 0 || character >= _characters.Count)
					break;

				Character c = _characters[(int)character];

				if (enabled == 1)
				{
					c.OnFilterChange(true);
					c.OnFullStateBlock(iteration, b);
				}
				else
				{
					c.OnFilterChange(false);
				}
			}
		}

		public uint GetPredictedTime()
		{
			TimeSpan ts = DateTime.Now - _startTime;
			return _startIteration + (uint)(1000.0 * ((double)ts.Ticks / (double)TimeSpan.TicksPerSecond));
		}

		public uint GetClientTime()
		{
			TimeSpan ts = DateTime.Now - _startTime;
			return (uint)(1000.0 * ((double)ts.Ticks / (double)TimeSpan.TicksPerSecond));
		}
	
		private void OnServerTimestamp(uint iteration)
		{
			// play catch up
			int diff = (int)iteration - (int)GetPredictedTime();
			if (diff > 0)
			{
				_startIteration += (uint)diff;
				Debug.Log("Adjusting local time by " + diff + " ticks ahead");
			}
		}
			
		private ItemInstance ReadInventoryItem(Bitstream.Buffer b)
		{
			ItemInstance item = new ItemInstance();
			item.Id = Bitstream.ReadCompressedUint(b);
			item.TypeId = Bitstream.ReadCompressedUint(b);
			item.Count = Bitstream.ReadCompressedUint(b);
			item.Slot = Bitstream.ReadCompressedUint(b);
			item.UserState = Bitstream.ReadCompressedUint(b);
			item.ChildCount = Bitstream.ReadCompressedUint(b);
			if (item.ChildCount > 0)
			{
				uint included = Bitstream.ReadBits(b, 1);
				if (included != 0)
				{
					item.Children = new List<ItemInstance>();
					for (uint i=0;i!=item.ChildCount;i++)
					{
						item.Children.Add(ReadInventoryItem(b));
					}
				}
			}

			if (_itemDataResolver != null)
				item.ItemData = _itemDataResolver(item.TypeId);

			_itemInstances[item.Id] = item;
			return item;
		}

		private void OnUpdatePlayersBlock(Bitstream.Buffer b)
		{
			Debug.Log("Players update block");
			_players = new List<Player>();
			_self = null;
			uint slots = Bitstream.ReadCompressedUint(b);
			for (uint i = 0; i < slots; i++)
			{
				Player p = new Player();
				p.Id = Bitstream.ReadCompressedUint(b);
				p.Name = Bitstream.ReadStringDumb(b);
				p.IsSelf = Bitstream.ReadBits(b, 1) != 0;
				if (p.IsSelf)
				{
					_self = p;
					p.Inventory = new List<ItemInstance>();
					uint invcount = Bitstream.ReadCompressedUint(b);
					Debug.Log(p.Name + " has inventory items " + invcount);
					for (uint j = 0; j < invcount; j++)
					{
						p.Inventory.Add(ReadInventoryItem(b));
					}
				}
				_players.Add(p);
			}
		}

		private void OnUpdateEntityBlock(Bitstream.Buffer b)
		{
			uint iteration = Bitstream.ReadCompressedUint(b);

			while (b.BitsLeft() > 0)
			{
				uint entityId = Bitstream.ReadCompressedUint(b);
				Bitstream.SyncByte(b);
				if (!_entities.ContainsKey(entityId))
				{
					Debug.Log("Got invalid entity id in update " + entityId);
					break;
				}
				_entities[entityId].OnUpdateBlock(iteration, b);
				Bitstream.SyncByte(b);
			}
		}

		private void OnUpdateCharactersBlock(Bitstream.Buffer b)
		{
			uint iteration = Bitstream.ReadCompressedUint(b);
			OnServerTimestamp(iteration);
			while (true)
			{
				uint character = Bitstream.ReadBits(b, 16);
				if (b.error != 0 || character >= _characters.Count)
					break;

				Bitstream.SyncByte(b);

				Character c = _characters[(int)character];
				c.OnUpdateBlock(iteration, b);

				Bitstream.SyncByte(b);
			}
		}

		public Bitstream.Buffer PollPlayerEvent()
		{
			if (_player_events.Count > 0)
			{
				Bitstream.Buffer b = _player_events[0];
				_player_events.RemoveAt(0);
				return b;
			}
			return null;
		}

		private void OnLanePacket(Bitstream.Buffer b, bool reliable)
		{
			DatagramCoding.Type type = (DatagramCoding.Type)Bitstream.ReadBits(b, DatagramCoding.TYPE_BITS);

			if (b.error != 0)
				return;

			if (type == DatagramCoding.Type.PLAYER_EVENT)
			{
				_player_events.Add(b);
				return;
			}

			if (type == DatagramCoding.Type.UPDATE)
			{
				UpdateBlock.Type subtype = (UpdateBlock.Type) Bitstream.ReadBits(b, UpdateBlock.TYPE_BITS);
				if (b.error != 0)
					return;
				switch (subtype)
				{
					case UpdateBlock.Type.FILTER:
						OnUpdateFilterBlock(b);
						break;
					case UpdateBlock.Type.CHARACTERS:
						OnUpdateCharactersBlock(b);
						break;
					case UpdateBlock.Type.PLAYERS:
						OnUpdatePlayersBlock(b);
						break;
					case UpdateBlock.Type.ENTITIES:
						OnUpdateEntityBlock(b);
						break;
					default:
						break;
				}
			}
		}

		private void OnStreamPacket(Packet p)
		{
			switch (p.type_id)
			{
				case MMOHumanAttachController.TYPE_ID:
					{
						MMOHumanAttachController attach = (MMOHumanAttachController)p;
						if (attach.Character < (uint)_characters.Count)
						{
							_controlled = _characters[(int)attach.Character];
						}
						else
						{
							_controlled = null;
						}
					}
					break;
				default:
					break;
			}
		}

		private void SendLanePacket(byte lane, Bitstream.Buffer buf)
		{
			GameNodeRawDatagramWrapper wrap = new GameNodeRawDatagramWrapper();
			wrap.Data = new byte[1024];
			wrap.Data[0] = lane;
			wrap.Length = (buf.bufsize - buf.bytepos) + 1;
			wrap.Offset = 0;
			System.Buffer.BlockCopy(buf.buf, 0, wrap.Data, 1, buf.bufsize - buf.bytepos + 1);
			_client.Send(wrap, false);
		}

		public void Update(float deltaTime)
		{
			Packet[] packets = _client.ReadPackets();
			foreach (Packet p in packets)
			{
				if (p.type_id == GameNodeRawDatagramWrapper.TYPE_ID)
				{
					GameNodeRawDatagramWrapper wrap = (GameNodeRawDatagramWrapper)p;
					Bitstream.Buffer buf = new Bitstream.Buffer();
					buf.buf = wrap.Data;
					buf.bufsize = wrap.Length + wrap.Offset;
					buf.bytepos = wrap.Offset + 1;
					if (wrap.Data[wrap.Offset] == 0)
					{
						_pl_reliable.Incoming(buf);
					}
					else if (wrap.Data[wrap.Offset] == 1)
					{
						_pl_unreliable.Incoming(buf);
					}
				}
				else
				{
					OnStreamPacket(p);
				}
			}

			// packet loops.
			while (true)
			{
				Bitstream.Buffer buf = _pl_unreliable.Update(-1, null);
				if (buf == null)
					break;
				OnLanePacket(buf, false);
			}

			while (true)
			{
				Bitstream.Buffer buf = _pl_reliable.Update(-1, null);
				if (buf == null)
					break;
				OnLanePacket(buf, true);
			}

			_pl_reliable.Update(deltaTime, delegate(netki.Bitstream.Buffer buf) {
				SendLanePacket(0, buf);
			});

			_pl_unreliable.Update(deltaTime, delegate(netki.Bitstream.Buffer buf) {
				SendLanePacket(1, buf);
			});
		}

		public List<Player> GetPlayerTable()
		{
			return _players;
		}

		public Player GetPlayerSelf()
		{
			return _self;
		}

		public void Reload()
		{
			Bitstream.Buffer cmd = Bitstream.Buffer.Make(new byte[128]);
			DatagramCoding.WriteCharacterEventBlockHeader(cmd, EventBlock.Type.RELOAD);
			cmd.Flip();
			_pl_reliable.Send(cmd);
		}

		public void Fire(uint itemInstanceId, uint characterId, string hitTarget, Vector3 localModelHit)
		{
			Bitstream.Buffer cmd = Bitstream.Buffer.Make(new byte[128]);
			DatagramCoding.WriteCharacterEventBlockHeader(cmd, EventBlock.Type.FIRE);
			Bitstream.PutCompressedUint(cmd, itemInstanceId);
			Bitstream.PutStringDumb(cmd, hitTarget);
			Bitstream.PutCompressedUint(cmd, characterId);
			NetUtil.PutScaledVec3(cmd, 0.001f, localModelHit);
			cmd.Flip();
			_pl_reliable.Send(cmd);
		}

		public void Equip(uint itemInstanceId)
		{
			Bitstream.Buffer cmd = Bitstream.Buffer.Make(new byte[128]);
			DatagramCoding.WritePlayerEventBlockHeader(cmd, EventBlock.Type.ITEM_EQUIP);
			Bitstream.PutBits(cmd, 1, 1);
			Bitstream.PutCompressedUint(cmd, itemInstanceId);
			cmd.Flip();
			_pl_reliable.Send(cmd);
		}

		public void Unequip(uint itemInstanceId)
		{
			Bitstream.Buffer cmd = Bitstream.Buffer.Make(new byte[128]);
			DatagramCoding.WritePlayerEventBlockHeader(cmd, EventBlock.Type.ITEM_EQUIP);
			Bitstream.PutBits(cmd, 1, 0);
			Bitstream.PutCompressedUint(cmd, itemInstanceId);
			cmd.Flip();
			_pl_reliable.Send(cmd);
		}

		public void MoveItem(uint itemInstanceId, uint newSlot)
		{
			Bitstream.Buffer cmd = Bitstream.Buffer.Make(new byte[128]);
			DatagramCoding.WritePlayerEventBlockHeader(cmd, EventBlock.Type.ITEM_MOVE);
			Bitstream.PutCompressedUint(cmd, itemInstanceId);
			Bitstream.PutCompressedUint(cmd, newSlot);
			cmd.Flip();
			_pl_reliable.Send(cmd);
		}

		public void UseItem(uint itemInstanceId)
		{
			Bitstream.Buffer cmd = Bitstream.Buffer.Make(new byte[128]);
			DatagramCoding.WritePlayerEventBlockHeader(cmd, EventBlock.Type.ITEM_USE);
			Bitstream.PutCompressedUint(cmd, itemInstanceId);
			cmd.Flip();
			_pl_reliable.Send(cmd);
		}

		public void SetItemState(uint itemInstanceId, uint newState)
		{
			Bitstream.Buffer cmd = Bitstream.Buffer.Make(new byte[128]);
			DatagramCoding.WritePlayerEventBlockHeader(cmd, EventBlock.Type.ITEM_SET_STATE);
			Bitstream.PutCompressedUint(cmd, itemInstanceId);
			Bitstream.PutCompressedUint(cmd, newState);
			cmd.Flip();
			_pl_reliable.Send(cmd);
		}

		public void DropItem(uint itemInstanceId)
		{
			Bitstream.Buffer cmd = Bitstream.Buffer.Make(new byte[128]);
			DatagramCoding.WritePlayerEventBlockHeader(cmd, EventBlock.Type.ITEM_DROP);
			Bitstream.PutCompressedUint(cmd, itemInstanceId);
			cmd.Flip();
			_pl_reliable.Send(cmd);
		}

		public void Interact(uint entityId)
		{
			Bitstream.Buffer cmd = Bitstream.Buffer.Make(new byte[128]);
			DatagramCoding.WriteCharacterEventBlockHeader(cmd, EventBlock.Type.INTERACT);
			Bitstream.PutCompressedUint(cmd, entityId);
			cmd.Flip();
			_pl_reliable.Send(cmd);
		}

		// commands
		public void DoSpawnCharacter(string id)
		{
			Bitstream.Buffer cmd = Bitstream.Buffer.Make(new byte[128]);
			DatagramCoding.WriteCharacterEventBlockHeader(cmd, EventBlock.Type.SPAWN);
			Bitstream.PutStringDumb(cmd, id);
			Bitstream.PutCompressedUint(cmd, GetClientTime());
			cmd.Flip();

			Debug.Log("Spawning [" + id + "]");

			string k = "";
			for (int i = 0; i < cmd.bufsize; i++)
				k = k + "[" + cmd.buf[i] + "] ";
			Debug.Log("pkt = " + k);

			_pl_reliable.Send(cmd);
		}

		public void SendReliable(Bitstream.Buffer buf)
		{
			_pl_reliable.Send(buf);
		}

		public void SendUnreliable(Bitstream.Buffer buf)
		{
			_pl_unreliable.Send(buf);
		}
	}
}
