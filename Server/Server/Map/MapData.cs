﻿using Newtonsoft.Json;
using NLog;
using Server.Utility;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Server.Map
{
    public class MapData
    {
        private static Logger s_log = LogManager.GetCurrentClassLogger();

        //These algorithms don't play well with floats...
        private const int SCALE_FACTOR = 1000;

        //Path length needs to be squared
        private const double MAX_PATH_LENGTH = 50000.0 * 50000.0;

        public Polygon[] CollisionAreas { get; set; }
        public Waypoint[] Waypoints { get; set; }

        private PointKDTree<Waypoint> m_waypointTree = new PointKDTree<Waypoint>();

        private Pool<Dictionary<int, NodeInfo>> m_infoPool = new Pool<Dictionary<int, NodeInfo>>();
        private Pool<BinaryHeap<AStarNode>> m_heapPool = new Pool<BinaryHeap<AStarNode>>();
        private ConcurrentDictionary<long, List<int>> m_pathCache = new ConcurrentDictionary<long, List<int>>();

        private struct NodeInfo
        {
            public int ParentIndex;
            public float G;
        }

        private MapData()
        {
        }

        public static MapData LoadFromFile(string path)
        {
            MapData map;

            using (FileStream file = File.Open(path, FileMode.Open))
            {
                using (StreamReader reader = new StreamReader(file))
                {
                    map = JsonConvert.DeserializeObject<MapData>(reader.ReadToEnd());
                }
            }

            map.CalculateWaypoints();

            Waypoint[] waypointsCopy = new Waypoint[map.Waypoints.Length];
            Array.Copy(map.Waypoints, waypointsCopy, waypointsCopy.Length);
            map.m_waypointTree.Build(waypointsCopy);

            return map;
        }

        private void CalculateWaypoints()
        {
            DateTime startTime = DateTime.Now;

            Waypoints = new Waypoint[CollisionAreas.Sum(p => p.Points.Length)];

            int x = 0;
            foreach (Polygon poly in CollisionAreas)
            {
                for (int i = 0; i < poly.Points.Length; i++)
                {
                    poly.Points[i] = poly.Points[i] * SCALE_FACTOR;
                    Waypoints[x] = new Waypoint(x) { Position = poly.Points[i] };
                    x++;
                }

                poly.UpdateBounds();
            }

            x = 0;
            foreach (Polygon poly in CollisionAreas)
            {
                for (int i = 0, j = poly.Points.Length - 1; i < poly.Points.Length; j = i++)
                {
                    Waypoints[x + i].AddConnectionTo(Waypoints[x + j]);
                }

                x += poly.Points.Length;
            }

            Parallel.ForEach(Waypoints, (w1) =>
            {
                foreach (Waypoint w2 in Waypoints)
                {
                    if (!w1.IsConnectedTo(w2))
                    {
                        if (HasClearLineOfSight(w1.Position, w2.Position))
                        {
                            w1.AddConnectionTo(w2);
                        }
                    }
                }
            });

            TimeSpan calculationTime = DateTime.Now - startTime;

            s_log.Debug("Calculate waypoints took {0}", calculationTime);
        }

        public Bitmap RenderMap(int height)
        {
            BoundingBox bounds = BoundingBox.CreateFromPoints(CollisionAreas.SelectMany(p => p.Points));
            Vector2 scale = bounds.Max - bounds.Min;

            float aspect = scale.X / scale.Y;

            int width = (int)(height * aspect);

            Bitmap render = new Bitmap(width, height);

            Vector2 size = new Vector2(width - 24, height - 24);

            using (Graphics g = Graphics.FromImage(render))
            using (Font font = new Font("Arial", 24, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(Color.Red))
            {
                g.Clear(Color.White);

                Pen connectionPen = new Pen(Color.Green, 1);
                foreach (WaypointConnection connection in Waypoints.SelectMany(w => w.Connections))
                {
                    Point p1 = NormaliseVector(connection.Source.Position, bounds.Min, scale, size);
                    Point p2 = NormaliseVector(connection.Target.Position, bounds.Min, scale, size);

                    g.DrawLine(connectionPen, p1, p2);
                }

                Pen polyPen = new Pen(Color.Red, 3);
                foreach (Polygon poly in CollisionAreas)
                {
                    for (int i = 0, j = poly.Points.Length - 1; i < poly.Points.Length; j = i++)
                    {
                        Point p1 = NormaliseVector(poly.Points[i], bounds.Min, scale, size);
                        Point p2 = NormaliseVector(poly.Points[j], bounds.Min, scale, size);

                        g.DrawLine(polyPen, p1, p2);
                    }
                }

                Pen pathPen = new Pen(Color.Blue, 3);
                Vector2 from = Waypoints[0].Position + new Vector2(38000, 25000);
                Waypoint fwp = m_waypointTree.NearestNeighbour(from);
                List<int> path = CalculatePath(fwp, Waypoints[6]);
                Vector2 direction = GetDirection(from, Waypoints[6].Position) * 50000;
                for (int i = 0; i < path.Count - 1; i++)
                {
                    Point p1 = NormaliseVector(Waypoints[path[i]].Position, bounds.Min, scale, size);
                    Point p2 = NormaliseVector(Waypoints[path[i + 1]].Position, bounds.Min, scale, size);
                    PointF f = new PointF(p1.X, p1.Y);
                    g.DrawLine(pathPen, p1, p2);
                }

                Pen directionPen = new Pen(Color.Tomato, 5);
                g.DrawLine(directionPen, NormaliseVector(from, bounds.Min, scale, size), NormaliseVector(from + direction, bounds.Min, scale, size));
                g.DrawString("From", font, b, new PointF(NormaliseVector(from, bounds.Min, scale, size).X, NormaliseVector(from, bounds.Min, scale, size).Y));
            }
            return render;
        }

        private static Point NormaliseVector(Vector2 v, Vector2 offset, Vector2 scale, Vector2 size)
        {
            v -= offset;
            v /= scale;
            v *= size;

            v.Y = size.Y - v.Y;

            return new Point((int)v.X + 12, (int)v.Y + 12);
        }

        private bool HasClearLineOfSight(Vector2 from, Vector2 to)
        {
            if (Vector2.DistanceSquared(from, to) <= MAX_PATH_LENGTH)
            {
                Vector2 shortenedA = Vector2.Lerp(from, to, 0.0001f);
                Vector2 shortenedB = Vector2.Lerp(to, from, 0.0001f);

                LineSegment path = new LineSegment(shortenedA, shortenedB);

                Vector2 center = Vector2.Lerp(from, to, 0.5f);

                foreach (Polygon poly in CollisionAreas)
                {
                    for (int i = 0, j = poly.Points.Length - 1; i < poly.Points.Length; j = i++)
                    {
                        if ((poly.Points[i] == from && poly.Points[j] == to) ||
                            (poly.Points[j] == from && poly.Points[i] == to))
                        {
                            //Path is an edge on a collision area
                            return false;
                        }

                        if (poly.ContainsPoint(center))
                        {
                            //Path runs through a collision area
                            return false;
                        }

                        LineSegment edge = poly.Edges[i];
                        if (path.Intersects(edge))
                        {
                            //Path runs through a collision area
                            return false;
                        }
                    }
                }

                //Clear line of sight
                return true;
            }

            //Too far away
            return false;
        }

        public Vector2 GetDirection(Vector2 from, Vector2 to)
        {
            //No need for a path
            if (HasClearLineOfSight(from, to))
            {
                return Vector2.Normalize(to - from);
            }

            //Find a path
            Waypoint nearestFrom = m_waypointTree.NearestNeighbour(from);
            Waypoint nearestTo = m_waypointTree.NearestNeighbour(to);

            List<int> path = CalculatePath(nearestFrom, nearestTo);

            //Find the furthest node along the path with a clear LoS
            for (int i = path.Count - 1; i >= 0; i--)
            {
                Vector2 waypointPosition = Waypoints[path[i]].Position;
                if (from == waypointPosition)
                {
                    return from += Vector2.One;
                }

                if (HasClearLineOfSight(from, waypointPosition))
                {
                    return Vector2.Normalize(waypointPosition - from);
                }
            }


            //No path found
            return Vector2.Zero;
        }

        public List<int> CalculatePath(Waypoint from, Waypoint to)
        {
            //Switch the to and from order to avoid having to reverse the reconstructed path
            Waypoint tmp = from;
            from = to;
            to = tmp;

            List<int> path;

            //First try and fetch from cache
            long pathKey = ((long)from.Index << 32) | (uint)to.Index;
            if (m_pathCache.TryGetValue(pathKey, out path))
            {
                return path;
            }

            path = new List<int>();

            //Not cached. Will have to search...
            //Initialise data structures
            HashSet<int> closed = new HashSet<int>();
            BinaryHeap<AStarNode> open = m_heapPool.Take();
            Dictionary<int, NodeInfo> nodeInfo = m_infoPool.Take();

            //Cache all index lookups
            int fromIndex = from.Index;
            int toIndex = to.Index;

            //Push the starting node
            AStarNode startNode = new AStarNode();
            startNode.Index = fromIndex;
            startNode.H = Vector2.Distance(from.Position, to.Position);
            nodeInfo[fromIndex] = new NodeInfo() { G = 0, ParentIndex = -1 };
            startNode.F = startNode.H;

            open.Enqueue(startNode);

            while (open.Count > 0)
            {
                //Take node with lowest F score
                AStarNode currentNode = open.Dequeue();
                if (currentNode.Index == toIndex)
                {
                    ReconstructPath(path, nodeInfo, toIndex);
                    break;
                }

                //Get waypoint for the node we're looking at
                int currentIndex = currentNode.Index;
                Waypoint currentWaypoint = Waypoints[currentIndex];

                //Mark node visited
                closed.Add(currentIndex);

                //Search all connected nodes...
                int neighbourCount = currentWaypoint.Neighbours.Count;
                for (int i = 0; i < neighbourCount; i++)
                {
                    //Fetch the waypoint for the neighbour we're looking at
                    Waypoint neighbour = currentWaypoint.Neighbours[i];
                    int neighbourIndex = neighbour.Index;

                    //Skip if this has already been considered
                    if (closed.Contains(neighbourIndex))
                    {
                        continue;
                    }

                    //Calculate G score to neighbour along this path
                    float thisG = nodeInfo[currentIndex].G + Vector2.Distance(currentWaypoint.Position, neighbour.Position);

                    //Speculatively create the new node to consider
                    AStarNode newNode = new AStarNode();
                    newNode.Index = neighbourIndex;

                    //If we're not considering this node OR
                    //If we're already considering this node, but it has a better G score along this path
                    if ((nodeInfo.ContainsKey(neighbourIndex) && thisG < nodeInfo[neighbourIndex].G) || !open.Contains(newNode))
                    {
                        //Fill node details
                        newNode.G = thisG;
                        newNode.H = Vector2.Distance(neighbour.Position, to.Position);
                        newNode.F = newNode.G + newNode.H;

                        //Add (or update) it for consideration
                        open.Enqueue(newNode);

                        nodeInfo[neighbourIndex] = new NodeInfo() { ParentIndex = currentIndex, G = thisG };
                    }
                }
            }

            //Reset data structures
            nodeInfo.Clear();
            open.Clear();

            //Return to pools
            m_infoPool.Return(nodeInfo);
            m_heapPool.Return(open);

            //Cache path
            m_pathCache[pathKey] = path;

            return path;
        }

        private static void ReconstructPath(List<int> path, Dictionary<int, NodeInfo> visits, int current)
        {
            if (visits.ContainsKey(current))
            {
                path.Add(current);
                ReconstructPath(path, visits, visits[current].ParentIndex);
            }
        }
    }
}
