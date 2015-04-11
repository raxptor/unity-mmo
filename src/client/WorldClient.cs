using Cube;
using netki;
using System.Collections.Generic;

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

		private IGameInstClient _client;
		private Dictionary<int, Character> _characters;
		private PacketLaneReliableOrdered _pl_reliable;
		private PacketLaneUnreliableOrdered _pl_unreliable;
		private Character _controlled;

		public WorldClient(IGameInstClient client)
		{
			_client = client;
			_characters = new Dictionary<int, Character>();
			_pl_reliable = new PacketLaneReliableOrdered();
			_pl_unreliable = new PacketLaneUnreliableOrdered();
		}

		public void AddCharacter(int index, Character character)
		{
			_characters.Add(index, character);
		}

		public Character GetControlledCharacter()
		{
			return _controlled;
		}

		private void OnUpdateFilterBlock(Bitstream.Buffer b)
		{
			uint iteration = Bitstream.ReadBits(b, 24);

			while (true)
			{
				uint character = Bitstream.ReadBits(b, 15);
				uint enabled = Bitstream.ReadBits(b, 1);

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

		private void OnUpdateCharactersBlock(Bitstream.Buffer b)
		{
			uint iteration = Bitstream.ReadBits(b, 24);
			while (true)
			{
				uint character = Bitstream.ReadBits(b, 16);
				if (b.error != 0 || character >= _characters.Count)
					break;

				Character c = _characters[(int)character];
				c.OnUpdateBlock(iteration, b);
			}
		}

		private void OnLanePacket(Bitstream.Buffer b, bool reliable)
		{
			DatagramCoding.Type type = (DatagramCoding.Type)Bitstream.ReadBits(b, DatagramCoding.TYPE_BITS);

			if (b.error != 0)
				return;

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
			wrap.Length = buf.bufsize - buf.bytepos;
			wrap.Offset = buf.bytepos;
			System.Buffer.BlockCopy(buf.buf, 0, wrap.Data, 1, buf.bufsize);
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

		// commands
		public void DoSpawnCharacter(uint CharacterHash)
		{
			Bitstream.Buffer cmd = Bitstream.Buffer.Make(new byte[128]);
			DatagramCoding.WriteEventBlockHeader(cmd, EventBlock.Type.SPAWN);
			Bitstream.PutCompressedUint(cmd, CharacterHash);
			cmd.Flip();
			_pl_reliable.Send(cmd);
		}
	}
}
