using netki;
using System.Collections.Generic;

namespace UnityMMO
{
	public class Mesh
	{
		public struct Point
		{
			public Point(float _x, float _y)
			{
				x = _x;
				y = _y;
			}
			public float x;
			public float y;
		}

		public struct Poly
		{
			public int[] indices;
		}

		public Point[] m_verts;
		public Poly[] m_polys;

		public bool ContainsPoint(Poly p, Point v)
		{
			// check against all 'planes' along the edges
			for (int i=0;i<p.indices.Length;i++)
			{
				int a = i;
				int b = (i+1) % p.indices.Length;
				float nx =   (m_verts[b].y - m_verts[a].y);
				float ny = - (m_verts[b].x - m_verts[a].x);
				float px = v.x - m_verts[a].x;
				float py = v.y - m_verts[a].y;
				float dot = px*nx + py*ny;
				if (dot < 0)
					return false;
			}
			return true;
		}

		public bool SharesEdge(int p, int a, int b)
		{
			Poly p = m_polys[p];
			for (int i=0;i<p.indices.Length;i++)
			{
				int u = p.indices[i];
				int v = p.indices[(i+1) % p.indices.Length];
				if (u == a && v == b)
					return true;
				if (u == b && v == a)
					return true;
			}
			return false;
		}
	}

	public class NavMesh : ILevelQuery
	{
		public struct AdjacentPolys
		{
			int[] Polys;
		}

		public Mesh m_mesh;
		public AdjacentPolys m_adjacent;

		public NavMesh(Mesh m)
		{
			m_mesh = m;

			m_adjacent = new AdjacentPolys[m.m_polys.Length];
			List<int> adjacent = new List<int>();

			for (int i=0;i<m.m_polys.Length;i++)
			{
				Mesh.Poly p = m.m_polys[i];
				for (int j=0;j<p.indices.Length;j++)
				{
					// edges.
					int a = p.indices[j];
					int b = p.indices[(j + 1) % p.indices.Length];

					for (int k=0;k<m.m_polys.Length;k++)
					{
						if (k != i && m.SharesEdge(k, a, b))
							adjacent.Add(k);
					}
				}
				m_adjacent[i] = adjacent.ToArray();
				adjacent.Clear();
			}
		}

		public int WhichPoly(Mesh.Point point)
		{
			for (int i=0;i<m_mesh.m_polys.Length;i++)
			{
				if (m_mesh.ContainsPoint(m_mesh.m_polys[i], point))
					return i;
			}
			return -1;
		}

		public bool IsValid(Vector3 point)
		{
			return WhichPoly(point) >= 0;
		}

		public Vector[] Navigate(Vector3 start, Vector3 stop)
		{
			if (!IsValid(start))
				return null;
			if (!IsValid(stop))
				return null;

			int start_poly = WhichPoly(start);
			int stop_poly = WhichPoly(stop);
			return null;
		}

	}
}