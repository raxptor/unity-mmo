using netki;

namespace UnityMMO
{
	public static class NetUtil
	{
		public static void PutScaledVec3(Bitstream.Buffer buf, float scale, Vector3 vec)
		{
			Bitstream.PutCompressedInt(buf, (int)(vec.x / scale));
			Bitstream.PutCompressedInt(buf, (int)(vec.y / scale));
			Bitstream.PutCompressedInt(buf, (int)(vec.z / scale));
		}

		public static bool ReadScaledVec3(Bitstream.Buffer buf, float scale, out Vector3 vec)
		{
			vec.x = scale * Bitstream.ReadCompressedInt(buf);
			vec.y = scale * Bitstream.ReadCompressedInt(buf);
			vec.z = scale * Bitstream.ReadCompressedInt(buf);
			return buf.error != 0;
		}
	}
}