using Silk.NET.SDL;
using System;

namespace TheAdventure.Models;

public class TemporaryGameObject : RenderableGameObject
{
    public double Ttl { get; init; }
    public bool IsExpired => (DateTimeOffset.Now - _spawnTime).TotalSeconds >= Ttl;
    public bool SpreadsFire { get; } // new property

    private DateTimeOffset _spawnTime;

    public TemporaryGameObject(SpriteSheet spriteSheet, double ttl, (int X, int Y) position,
                               bool spreadsFire, // new parameter
                               double angle = 0.0, Point rotationCenter = new())
        : base(spriteSheet, position, angle, rotationCenter)
    {
        Ttl = ttl;
        SpreadsFire = spreadsFire;
        _spawnTime = DateTimeOffset.Now;
    }
}