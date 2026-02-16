using Microsoft.Xna.Framework;

namespace HouseSidePlugin
{
    public class HouseData
    {
        public int CenterX { get; set; }
        public int CenterY { get; set; }
        public Rectangle Bounds { get; set; }
        public string Side { get; set; }
        public bool IsOccupied { get; set; }

        public HouseData()
        {
            IsOccupied = false;
        }

        public override string ToString()
        {
            return $"House at ({CenterX}, {CenterY}) on {Side} side, Bounds: {Bounds}, Occupied: {IsOccupied}";
        }
    }
}
