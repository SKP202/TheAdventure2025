using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheAdventure.Models;
using Silk.NET.SDL;

namespace TheAdventure.Models
{
    public class SkeletonObject : RenderableGameObject
    {
        private const int Speed = 64; // pixels per second

        public SkeletonObject(SpriteSheet spriteSheet, (int X, int Y) position)
            : base(spriteSheet, position) { }

        public void Update((int X, int Y) playerPos, double msSinceLastFrame)
        {
            var dx = playerPos.X - Position.X;
            var dy = playerPos.Y - Position.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist < 1.0)
                return;

            var moveDist = Speed * (msSinceLastFrame / 1000.0);
            var nx = Position.X + (int)(moveDist * dx / dist);
            var ny = Position.Y + (int)(moveDist * dy / dist);

            Position = (nx, ny);
        }
    }
}
