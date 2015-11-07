using netki;

namespace UnityMMO
{
	public static class GameInfo
	{
		public const uint Version = 2;
	}
	
	public static class UpdateBlock
	{
		public const int TYPE_BITS = 4;

		public enum Type
		{
			FILTER = 1,
			CHARACTERS = 2,
			PLAYERS = 3,
			ENTITIES = 4,
		}
	}

	// these should be sent reliably.
	public static class EventBlock
	{
		public const int TYPE_BITS = 10;

		public enum Type
		{
			SPAWN = 0,
			MOVE,
			FIRE,
			RELOAD,
			INTERACT,
			ACQUIRED_ITEM,
			ITEM_EQUIP,
			ITEM_DROP,
			ITEM_USE,
			ITEM_MOVE,
			CRAFT,
			HIT
		}
	}
		
	public static class DatagramCoding
	{
		public const int TYPE_BITS = 3;

		public enum Type
		{
			CHARACTER_EVENT   = 0,
			PLAYER_EVENT      = 1,
			UPDATE            = 2,
			PACKET            = 3
		}

		public static void WriteCharacterEventBlockHeader(Bitstream.Buffer buf, EventBlock.Type st)
		{
			Bitstream.PutBits(buf, TYPE_BITS, (uint)Type.CHARACTER_EVENT);
			Bitstream.PutBits(buf, EventBlock.TYPE_BITS, (uint)st);
		}

		public static void WritePlayerEventBlockHeader(Bitstream.Buffer buf, EventBlock.Type st)
		{
			Bitstream.PutBits(buf, TYPE_BITS, (uint)Type.PLAYER_EVENT);
			Bitstream.PutBits(buf, EventBlock.TYPE_BITS, (uint)st);
		}

		public static void WriteUpdateBlockHeader(Bitstream.Buffer buf, UpdateBlock.Type st)
		{
			Bitstream.PutBits(buf, TYPE_BITS, (uint)Type.UPDATE);
			Bitstream.PutBits(buf, UpdateBlock.TYPE_BITS, (uint)st);
		}
	}
}
