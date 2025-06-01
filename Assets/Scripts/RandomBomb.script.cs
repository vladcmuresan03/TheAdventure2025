using TheAdventure.Scripting;
using System;
using TheAdventure;

public class RandomBomb : IScript
{
    DateTimeOffset _nextBombTimestamp;

    private double _minDelay = 2.0;
    private double _maxDelay = 5.0;
    private double _minPossibleDelay = 0.5;
    private double _decreaseTime = 0.10;

    public void Initialize()
    {
        ScheduleNextBomb(0);
    }

    private void ScheduleNextBomb(double timeAlive)
    {
        double currentMaxDelay = Math.Max(_minPossibleDelay + 0.1, _maxDelay - (timeAlive * _decreaseTime));
        double currentMinDelay = Math.Max(_minPossibleDelay, currentMaxDelay / 2.0);

        if (currentMinDelay > currentMaxDelay)
        {
            currentMinDelay = currentMaxDelay * 0.9;
        }

        double randomDelay = currentMinDelay + (Random.Shared.NextDouble() * (currentMaxDelay - currentMinDelay));

        _nextBombTimestamp = DateTimeOffset.UtcNow.AddSeconds(randomDelay);
    }

    public void Execute(Engine engine)
    {
        if (_nextBombTimestamp < DateTimeOffset.UtcNow)
        {

            double timeAlive = engine.GetTimeAlive();
            ScheduleNextBomb(timeAlive);

            var playerPos = engine.GetPlayerPosition();
            var bombPosX = playerPos.X + Random.Shared.Next(-50, 50);
            var bombPosY = playerPos.Y + Random.Shared.Next(-50, 50);

            // Call AddBomb => spreadsFire = true (fire bomb); translateCoordinates = false
            engine.AddBomb(bombPosX, bombPosY, true, false);
        }
    }
}