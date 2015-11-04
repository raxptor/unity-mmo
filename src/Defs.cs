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
			PLAYERS = 3
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
			CHANGE_WEAPON
		}
	}
		
	public static class DatagramCoding
	{
		public const int TYPE_BITS = 3;

		public enum Type
		{
			EVENT   = 0,
			UPDATE  = 1,
			PACKET  = 2
		}

		public static void WriteEventBlockHeader(Bitstream.Buffer buf, EventBlock.Type st)
		{
			Bitstream.PutBits(buf, TYPE_BITS, (uint)Type.EVENT);
			Bitstream.PutBits(buf, EventBlock.TYPE_BITS, (uint)st);
		}

		public static void WriteUpdateBlockHeader(Bitstream.Buffer buf, UpdateBlock.Type st)
		{
			Bitstream.PutBits(buf, TYPE_BITS, (uint)Type.UPDATE);
			Bitstream.PutBits(buf, UpdateBlock.TYPE_BITS, (uint)st);
		}
	}
}
