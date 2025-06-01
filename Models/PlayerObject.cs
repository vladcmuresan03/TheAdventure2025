using Silk.NET.Maths;
using System;

namespace TheAdventure.Models;

public class PlayerObject : RenderableGameObject
{
    private const int _speed = 128; // pixels per second

    public enum PlayerStateDirection
    {
        None = 0,
        Down,
        Up,
        Left,
        Right,
    }

    public enum PlayerState
    {
        None = 0,
        Idle,
        Move,
        Attack,
        GameOver
    }

    public (PlayerState State, PlayerStateDirection Direction) State { get; private set; }

    public PlayerObject(SpriteSheet spriteSheet, int x, int y) : base(spriteSheet, (x, y))
    {
        SetState(PlayerState.Idle, PlayerStateDirection.Down);
    }

    public void SetState(PlayerState state)
    {
        SetState(state, State.Direction);
    }

    public void SetState(PlayerState state, PlayerStateDirection direction)
    {
        if (State.State == PlayerState.GameOver)
        {
            return;
        }

        if (State.State == state && State.Direction == direction)
        {
            return;
        }

        if (state == PlayerState.None && direction == PlayerStateDirection.None)
        {
            SpriteSheet.ActivateAnimation(null);
        }

        else if (state == PlayerState.GameOver)
        {
            SpriteSheet.ActivateAnimation(Enum.GetName(state));
        }
        else
        {
            var animationName = Enum.GetName(state) + Enum.GetName(direction);
            SpriteSheet.ActivateAnimation(animationName);
        }

        State = (state, direction);
    }

    public void GameOver()
    {
        SetState(PlayerState.GameOver, PlayerStateDirection.None);
    }

    public void Attack()
    {
        if (State.State == PlayerState.GameOver)
        {
            return;
        }

        var direction = State.Direction;
        SetState(PlayerState.Attack, direction);
    }

    public void UpdatePosition(double up, double down, double left, double right, int worldMinX, int worldMinY, int worldMaxX, int worldMaxY, double time)
    {
        if (State.State == PlayerState.GameOver)
        {
            return;
        }

        var pixelsToMove = _speed * (time / 1000.0);

        var x = Position.X + (int)(right * pixelsToMove);
        x -= (int)(left * pixelsToMove);

        var y = Position.Y + (int)(down * pixelsToMove);
        y -= (int)(up * pixelsToMove);

        var newState = State.State;
        var newDirection = State.Direction;

        // Get player dimensions (ensure SpriteSheet is not null)
        int playerWidth = SpriteSheet?.FrameWidth ?? 0;
        int playerHeight = SpriteSheet?.FrameHeight ?? 0;
        int halfPlayerWidth = playerWidth / 2;
        int halfPlayerHeight = playerHeight / 2;

        x = Math.Max(worldMinX + halfPlayerWidth, x);
        x = Math.Min(worldMaxX - halfPlayerWidth, x);
        y = Math.Max(worldMinY + halfPlayerHeight, y);
        y = Math.Min(worldMaxY - halfPlayerHeight, y);

        bool hasMovementInput = (up > 0 || down > 0 || left > 0 || right > 0);

        if (x == Position.X && y == Position.Y)
        {
            if (State.State == PlayerState.Attack)
            {
                if (SpriteSheet.AnimationFinished)
                {
                    newState = PlayerState.Idle;
                }
            }
            else
            {
                newState = PlayerState.Idle;
            }
        }
        else if (hasMovementInput) // if there IS movement input
        {
            newState = PlayerState.Move;

            if (up > 0) newDirection = PlayerStateDirection.Up;
            else if (down > 0) newDirection = PlayerStateDirection.Down;
            else if (left > 0) newDirection = PlayerStateDirection.Left;
            else if (right > 0) newDirection = PlayerStateDirection.Right;
        }

        else // No movement input, but position might have changed (e.g. pushed) or attack finished
        {
            newState = PlayerState.Idle; // Default to idle if not attacking and no input
            if (State.State == PlayerState.Attack && SpriteSheet.AnimationFinished)
            {
            }
            else if (State.Direction == PlayerStateDirection.None)
            {
                newDirection = PlayerStateDirection.Down;
            }
        }

        // If an attack just finished, it might have set newState to Idle.
        // If there was also movement input, newState should be Move.
        if (State.State == PlayerState.Attack && SpriteSheet.AnimationFinished && hasMovementInput)
        {
            newState = PlayerState.Move;
            // Direction is already set by hasMovementInput block above
        }


        if (newState != State.State || newDirection != State.Direction)
        {
            SetState(newState, newDirection);
        }

        Position = (x, y);
    }
}