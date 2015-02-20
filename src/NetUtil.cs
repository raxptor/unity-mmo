using netki;

namespace UnityMMO
{
	public static class NetUtil
	{
		public static void PutScaledVec3(Bitstream.Buffer buf, float scale, Vector3 vec)
		{

		}

		public static bool ReadScaledVec3(Bitstream.Buffer buf, float scale, out Vector3 vec)
		{
			vec.x = 0;
			vec.y = 0;
			vec.z = 0;
			return buf.error != 0;
		}
	}
}