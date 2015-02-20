using netki;
using System.Collections.Generic;

namespace UnityMMO
{
	public class GameInstServer : Cube.IGameInstServer
	{
		public class Slot
		{
			public string PlayerId;
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
		string _version;

		public GameInstServer(string version, int maxPlayers)
		{
			_version = version;
			_maxPlayers = maxPlayers;
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
				_worldServer.StopControlling(s.ServerCharacter);
				s.ServerCharacter = null;
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
					s = new Slot();
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

				// got contror of a character.
				netki.MMOHumanAttachController status = new netki.MMOHumanAttachController();
				status.Character = s.ServerCharacter.Data.Id;
				s.SendStream(status);

				if (s.Observer != null)
					_worldServer.RemoveObserver(s.Observer);

				s.Observer = _worldServer.AddObserver();
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
					if (length < 2)
						return false;

					if (datagram[offset] == 0x1 || datagram[offset] == 0x2)
					{
						Bitstream.Buffer buf = new Bitstream.Buffer();
						buf.buf = datagram;
						buf.bytepos = 1;
						buf.bufsize = length - 1;
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
			}
		}

		// Packet arrived through either reliable or unreilable lan.
		private void OnLanePacket(Slot s, Bitstream.Buffer b, bool reliable)
		{
			uint type = Bitstream.ReadBits(b, DatagramCoding.TYPE_BITS);
			if (b.error != 0)
				return;

			switch (type)
			{
				case DatagramCoding.TYPE_CONTROL:
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
						foreach (Bitstream.Buffer buf in s.Observer.UpdatesReliable)
							s.PacketLaneReliable.Send(buf);
						s.Observer.UpdatesReliable.Clear();

						foreach (Bitstream.Buffer buf in s.Observer.UpdatesUnreliable)
							s.PacketLaneUnreliable.Send(buf);
						s.Observer.UpdatesUnreliable.Clear();

						netki.PacketLaneOutput transmit0 = delegate(netki.Bitstream.Buffer buf)
						{
							buf.Flip();
							wrap.Data = new byte[1024];
							wrap.Data[0] = 0;
							System.Buffer.BlockCopy(buf.buf, 0, wrap.Data, 1, buf.bufsize);
							s.SendDatagram(wrap);
						};

						netki.PacketLaneOutput transmit1 = delegate(netki.Bitstream.Buffer buf)
						{
							buf.Flip();
							wrap.Data = new byte[1024];
							wrap.Data[0] = 1;
							System.Buffer.BlockCopy(buf.buf, 0, wrap.Data, 1, buf.bufsize);
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