using Microsoft.Xna.Framework;
using System;

namespace WaterBot.Framework
{
    /// <summary>
    /// Tile on the map, containing path finding and waterable related data.
    /// </summary>
    class Tile
    {
        public bool block;
        public bool water;
        public bool waterable;
        public int x;
        public int y;

        public Tile(int x, int y, bool block, bool water, bool waterable)
        {
            this.x = x;
            this.y = y;
            this.block = block;
            this.water = water;
            this.waterable = waterable;
        }

        /// <summary>
        /// Returns Point instance of Tile
        /// </summary>
        public Point getPoint()
        {
            return new Point(this.x, this.y);
        }

        public bool Equals(Tile other)
        {
            return other.x == this.x && other.y == this.y;
        }

        public bool Equals(Point other)
        {
            return other.X == this.x && other.Y == this.y;
        }

        public override string ToString()
        {
            if (this.water)
            {
                return "~";
            }
            if (this.waterable && this.block)
            {
                return "0";
            }
            if (this.waterable)
            {
                return "O";
            }
            if (this.block)
            {
                return "#";
            }
            return " ";
        }
    }
}
