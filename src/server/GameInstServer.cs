using netki;
using System.Collections.Generic;

namespace UnityMMO
{
	public class GameInstServer : Cube.IGameInstServer
	{
		public class Slot
		{
			public string PlayerId;
			public ServerPlayer Player;
			public ulong Endpoint;
			public Cube.GameInstPlayer GameInstPlayer;
			public Cube.PacketExchangeDelegate SendDatagram, SendStream;
			public PacketLaneReliableOrdered PacketLaneReliable;
			public PacketLaneUnreliableOrdered PacketLaneUnreliable;
			public UnityMMO.ServerHumanController Controller;
			public UnityMMO.WorldObserver Observer;
			public UnityMMO.ServerCharacter ServerCharacter;
		}

		public WorldServer _worldServer;
		List<Slot> _slots = new List<Slot>();
		int _maxPlayers;
		uint _playerIdCounter = 0;
		string _version;
		private PlayerDataSynchronizer _dataSynchronizer;


		public GameInstServer(WorldServer srv, PlayerDataSynchronizer dataSynchronizer, string version, int maxPlayers)
		{
			_version = version;
			_maxPlayers = maxPlayers;
			_worldServer = srv;
			_dataSynchronizer = dataSynchronizer; 
		}

		public bool CanPlayerReconnect(string playerId)
		{
			return false;
		}

		private Slot SlotByPlayerId(string id)
		{
			foreach (Slot s in _slots)
			{
				if (s.PlayerId == id)
					return s;
			}
			return null;
		}

		private Slot SlotByEndpoint(ulong endpoint)
		{
			foreach (Slot s in _slots)
			{
				if (s.Endpoint == endpoint)
					return s;
			}
			return null;
		}

		public void ResetSlot(Slot s)
		{
			if (s.Observer != null)
			{
				_worldServer.RemoveObserver(s.Observer);
				s.Observer = null;
			}
			if (s.Controller != null)
			{
				s.Controller = null;
			}
			if (s.ServerCharacter != null)
			{
				if (_dataSynchronizer != null)
				{
					_dataSynchronizer.SavePlayer(s.PlayerId, 0, s.Player, s.ServerCharacter);
				}
				_worldServer.StopControlling(s.ServerCharacter);
				s.ServerCharacter = null;
			}
		}

		private void SendPlayerTable(PacketLane to, ServerPlayer self)
		{
			Bitstream.Buffer b = Bitstream.Buffer.Make(new byte[1024]);
			DatagramCoding.WriteUpdateBlockHeader(b, UpdateBlock.Type.PLAYERS);
			Bitstream.PutCompressedUint(b, (uint)_slots.Count);
			for (int i = 0; i < _slots.Count; i++)
			{
				Slot s = _slots[i];
				s.Player.WriteFullState(b, self == s.Player);
			}
			b.Flip();
			to.Send(b);
		}

		private void SendPlayerTables()
		{
			foreach (Slot s in _slots)
			{
				if (s.PacketLaneReliable != null)
				{
					SendPlayerTable(s.PacketLaneReliable, s.Player);
				}
			}
		}

		public bool ConnectPlayerStream(string playerId, Cube.GameInstPlayer player, Cube.PacketExchangeDelegate _send_to_me)
		{
			lock (this)
			{
				Slot s = SlotByPlayerId(playerId);
				if (s == null)
				{
					if (_slots.Count >= _maxPlayers)
						return false;
					// new 
					s = new Slot();
					_slots.Add(s);
				}

				if (s.PlayerId == null)
				{
					s.Player = _worldServer.MakeNewPlayer(playerId, ++_playerIdCounter);
				}
	
				s.PlayerId = playerId;
				s.GameInstPlayer = player;
				s.SendStream = _send_to_me;
				s.SendDatagram = null;
				ResetSlot(s);
				return true;
			}
		}

		private bool TryEnterGame(Slot s)
		{
			if (s.SendStream == null || s.SendDatagram == null)
				return false;

			if (s.Controller == null)
			{
				s.Controller = new ServerHumanController();
			}

			if (s.ServerCharacter == null)
			{
				s.ServerCharacter = _worldServer.GrabHumanControllable(s.Controller);
				if (s.ServerCharacter == null)
					return false;

				s.ServerCharacter.ResetFromData(s.ServerCharacter.Data);
				s.ServerCharacter.Player = s.Player;

				if (_dataSynchronizer != null)
				{
					_dataSynchronizer.RestorePlayer(s.PlayerId, s.Player, s.ServerCharacter);
				}
				else
				{
					_worldServer.ResetCharacter(s.Player, s.ServerCharacter);
				}

				// got contror of a character.
				netki.MMOHumanAttachController status = new netki.MMOHumanAttachController();
				status.Character = s.ServerCharacter.Data.Id;
				s.SendStream(status);

				if (s.Observer != null)
					_worldServer.RemoveObserver(s.Observer);

				s.Observer = _worldServer.AddObserver(s.ServerCharacter);
			}
			return true;
		}

		public void ConnectPlayerDatagram(string playerId, ulong endpoint, Cube.PacketExchangeDelegate _send_to_me)
		{
			lock (this)
			{
				Slot s = SlotByPlayerId(playerId);
				if (s != null)
				{
					s.Endpoint = endpoint;
					s.SendDatagram = _send_to_me;
					s.PacketLaneReliable = new PacketLaneReliableOrdered();
					s.PacketLaneUnreliable = new PacketLaneUnreliableOrdered();
				}
				SendPlayerTables();
			}
		}
	
		public bool OnDatagram(byte[] datagram, int offset, int length, ulong endpoint)
		{			
			lock (this)
			{
				Slot s = SlotByEndpoint(endpoint);
				if (s != null)
				{
					// ---
					if (length < 1)
						return false;

					if (datagram[offset] == 0 || datagram[offset] == 1)
					{
						Bitstream.Buffer buf = new Bitstream.Buffer();
						buf.buf = datagram;
						buf.bytepos = offset + 1;
						buf.bufsize = offset + length;
						if (datagram[offset] == 0)
							s.PacketLaneReliable.Incoming(buf);
						else if (datagram[offset] == 1)
							s.PacketLaneUnreliable.Incoming(buf);
					}
				}
			}
			return true;
		}

		public void PacketOnPlayer(Cube.GameInstPlayer player, netki.Packet packet)
		{
			lock (this)
			{

			}
		}

		public void DisconnectPlayer(Cube.GameInstPlayer player)
		{
			lock (this)
			{
				Slot s = SlotByPlayerId(player.name);
				if (s != null)
				{
					ResetSlot(s);
					_slots.Remove(s);
				}
				SendPlayerTables();
			}
		}

		// Packet arrived through either reliable or unreilable lan.
		private void OnLanePacket(Slot s, Bitstream.Buffer b, bool reliable)
		{				
			uint type = Bitstream.ReadBits(b, DatagramCoding.TYPE_BITS);
			if (b.error != 0)
				return;

			switch ((DatagramCoding.Type)type)
			{
				case DatagramCoding.Type.PLAYER_EVENT:
					{
						ServerPlayerCommands.HandlePlayerEvent(s, b);
					}
					break;
						
				case DatagramCoding.Type.CHARACTER_EVENT:
					if (s.Controller != null)
					{
						s.Controller.OnControlBlock(b);
					}
					break;
				default:
					break;
			}
		}

		public void Update(float deltaTime)
		{
			netki.GameNodeRawDatagramWrapper wrap = new netki.GameNodeRawDatagramWrapper();

			lock (this)
			{
				// receive packets.
				foreach (Slot s in _slots)
				{
					TryEnterGame(s);

					if (s.PacketLaneReliable != null)
					{
						if (s.Player.InventoryChanged)
						{
							SendPlayerTable(s.PacketLaneReliable, s.Player);
							s.Player.InventoryChanged = false;
						}
						foreach (Bitstream.Buffer not in s.Player.Notifications)
						{
							s.PacketLaneReliable.Send(not);
						}
						s.Player.Notifications.Clear();

						Bitstream.Buffer tmp;
						while (true)
						{
							tmp = s.PacketLaneReliable.Update(-1, null);
							if (tmp == null)
								break;
							OnLanePacket(s, tmp, true);
						}
						while (true)
						{
							tmp = s.PacketLaneUnreliable.Update(-1, null);
							if (tmp == null)
								break;
							OnLanePacket(s, tmp, false);
						}
					}
				}

				// tick game
				_worldServer.Update(deltaTime);

				// send packets
				foreach (Slot s in _slots)
				{
					if (s.PacketLaneReliable != null)
					{
						// forward observer packets to lane
						if (s.Observer != null)
						{
							foreach (Bitstream.Buffer buf in s.Observer.UpdatesReliable)
								s.PacketLaneReliable.Send(buf);
							s.Observer.UpdatesReliable.Clear();

							foreach (Bitstream.Buffer buf in s.Observer.UpdatesUnreliable)
								s.PacketLaneUnreliable.Send(buf);
							s.Observer.UpdatesUnreliable.Clear();
						}

						netki.PacketLaneOutput transmit0 = delegate(netki.Bitstream.Buffer buf)
						{
							wrap = new netki.GameNodeRawDatagramWrapper();
							wrap.Data = new byte[1024];
							wrap.Data[0] = 0;
							System.Buffer.BlockCopy(buf.buf, 0, wrap.Data, 1, buf.bufsize);
							wrap.Length = buf.bufsize + 1;
							s.SendDatagram(wrap);
						};

						netki.PacketLaneOutput transmit1 = delegate(netki.Bitstream.Buffer buf)
						{
							wrap = new netki.GameNodeRawDatagramWrapper();
							wrap.Data = new byte[1024];
							wrap.Data[0] = 1;
							System.Buffer.BlockCopy(buf.buf, 0, wrap.Data, 1, buf.bufsize);
							wrap.Length = buf.bufsize + 1;
							s.SendDatagram(wrap);
						};
					
						// Cannot receive any here that was not received earlier.
						s.PacketLaneReliable.Update(deltaTime, transmit0);
						s.PacketLaneUnreliable.Update(deltaTime, transmit1);
					}
				}
			}
		}

		public netki.GameNodeGameStatus GetStatus()
		{
			lock (this)
			{
				netki.GameNodeGameStatus s = new netki.GameNodeGameStatus();
				s.PlayersJoined = (uint)_slots.Count;
				s.PlayerSlotsLeft = (uint)(_maxPlayers - _slots.Count);
				s.SpectatorSlotsLeft = 0;
				return s;
			}
		}

		public bool CanShutdown()
		{
			lock (this)
			{
				return _slots.Count == 0;
			}
		}

		public string GetVersionString()
		{
			return "mmoserv-1.0-" + _version;
		}
	}
}