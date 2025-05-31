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
        private const int Speed = 60; 
        private const double AttackRange = 24.0; 
        private const double AttackCooldownSeconds = 1.0; 
        private const double DeathAnimDuration = 0.7; 

        private DateTimeOffset _lastAttackTime = DateTimeOffset.MinValue;
        private double _fx, _fy;

        public bool IsDead { get; private set; } = false;
        private DateTimeOffset? _deathTime = null;

        public SkeletonObject(SpriteSheet spriteSheet, (int X, int Y) position)
            : base(spriteSheet, position)
        {
            _fx = position.X;
            _fy = position.Y;
        }

        
        public void Update(PlayerObject player, double msSinceLastFrame)
        {
            if (IsDead)
                return;

            var dx = player.Position.X - _fx;
            var dy = player.Position.Y - _fy;
            var dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist < AttackRange)
            {
                
                if ((DateTimeOffset.Now - _lastAttackTime).TotalSeconds >= AttackCooldownSeconds
                    && player.Health > 0)
                {
                    _lastAttackTime = DateTimeOffset.Now;
                    SpriteSheet.ActivateAnimation("AttackMelee"); 
                    player.TakeDamage(player.MaxHealth / 4);
                }
                else if (SpriteSheet.ActiveAnimation == null || SpriteSheet.ActiveAnimation != SpriteSheet.Animations["AttackMelee"])
                {
                    SpriteSheet.ActivateAnimation("AttackMelee");
                }
                return;
            }

            
            if (dist > 1.0)
            {
                var moveDist = Speed * (msSinceLastFrame / 1000.0);
                var moveRatio = moveDist / dist;
                if (moveRatio > 1.0) moveRatio = 1.0;
                _fx += dx * moveRatio;
                _fy += dy * moveRatio;

                Position = ((int)Math.Round(_fx), (int)Math.Round(_fy));

                if (SpriteSheet.ActiveAnimation == null || SpriteSheet.ActiveAnimation != SpriteSheet.Animations["Walk"])
                {
                    SpriteSheet.ActivateAnimation("Walk");
                }
            }
            else
            {
                
                if (SpriteSheet.ActiveAnimation == null || SpriteSheet.ActiveAnimation != SpriteSheet.Animations["Walk"])
                {
                    SpriteSheet.ActivateAnimation("Walk");
                }
            }
        }

        public void Kill()
        {
            if (IsDead)
                return;

            IsDead = true;
            _deathTime = DateTimeOffset.Now;
            SpriteSheet.ActivateAnimation("Death"); 
        }

        public bool ShouldRemove()
        {
            return IsDead && _deathTime.HasValue &&
                   (DateTimeOffset.Now - _deathTime.Value).TotalSeconds > DeathAnimDuration;
        }
    }
}
