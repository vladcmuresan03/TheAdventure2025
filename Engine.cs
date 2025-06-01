using System.Data;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Silk.NET.Maths;
using TheAdventure.Models;
using TheAdventure.Models.Data;
using TheAdventure.Scripting;

namespace TheAdventure;

public class Engine
{
    private readonly GameRenderer _renderer;
    private readonly Input _input;
    private readonly ScriptEngine _scriptEngine = new();

    private readonly Dictionary<int, GameObject> _gameObjects = new();
    private readonly Dictionary<string, TileSet> _loadedTileSets = new();
    private readonly Dictionary<int, Tile> _tileIdMap = new();

    private Level _currentLevel = new();
    private PlayerObject? _player;

    private DateTimeOffset _lastUpdate = DateTimeOffset.Now;

    private DateTimeOffset _lastWaterDropTime = DateTimeOffset.MinValue;
    private const double WATER_DROP_COOLDOWN_SECONDS = 5.0;

    private const int BURNING_TILE_GID = 5;
    private const int GRASS_TILE_GID = 1;
    private const int WALKABLE_LAYER_INDEX = 0;

    private double _timeAlive = 0.0;
    private bool _isAlive = true;

    public Engine(GameRenderer renderer, Input input)
    {
        _renderer = renderer;
        _input = input;

        _input.OnMouseClick += (_, coords) =>
        {
            if ((DateTimeOffset.Now - _lastWaterDropTime).TotalSeconds >= WATER_DROP_COOLDOWN_SECONDS)
            {
                // call AddBomb => spreadsFire = false; translateCoordinates = true;
                AddBomb(coords.x, coords.y, false, true);
                _lastWaterDropTime = DateTimeOffset.Now;
            }
        };
    }

    public void SetupWorld()
    {
        _gameObjects.Clear();
        _tileIdMap.Clear();
        _loadedTileSets.Clear();

        _player = new(SpriteSheet.Load(_renderer, "Player.json", "Assets"), 100, 100);

        var levelContent = File.ReadAllText(Path.Combine("Assets", "terrain.tmj"));
        var level = JsonSerializer.Deserialize<Level>(levelContent);
        if (level == null)
        {
            throw new Exception("Failed to load level");
        }
        _timeAlive = 0.0;
        _isAlive = true;

        foreach (var tileSetRef in level.TileSets)
        {
            var tileSetContent = File.ReadAllText(Path.Combine("Assets", tileSetRef.Source));
            var tileSet = JsonSerializer.Deserialize<TileSet>(tileSetContent);
            if (tileSet == null)
            {
                throw new Exception("Failed to load tile set");
            }

            foreach (var tile in tileSet.Tiles)
            {
                tile.TextureId = _renderer.LoadTexture(Path.Combine("Assets", tile.Image), out _);
                _tileIdMap.Add(tile.Id!.Value, tile);
            }

            _loadedTileSets.Add(tileSet.Name, tileSet);
        }

        if (level.Width == null || level.Height == null)
        {
            throw new Exception("Invalid level dimensions");
        }

        if (level.TileWidth == null || level.TileHeight == null)
        {
            throw new Exception("Invalid tile dimensions");
        }

        _renderer.SetWorldBounds(new Rectangle<int>(0, 0, level.Width.Value * level.TileWidth.Value,
            level.Height.Value * level.TileHeight.Value));

        _currentLevel = level;

        _scriptEngine.LoadAll(Path.Combine("Assets", "Scripts"));
    }

    public void ProcessFrame()
    {
        var currentTime = DateTimeOffset.Now;
        var msSinceLastFrame = (currentTime - _lastUpdate).TotalMilliseconds;
        _lastUpdate = currentTime;

        if (_player == null) return;

        // Check for game over condition first
        if (_player.State.State == PlayerObject.PlayerState.GameOver && _isAlive)
        {
            _isAlive = false;
            Console.WriteLine($"You lost! You survived for: {_timeAlive:F2} seconds. Press R to play again!");
        }

        if (!_isAlive && _input.IsKeyRPressed())
        {
            SetupWorld();
            _lastWaterDropTime = DateTimeOffset.MinValue;
            return;
        }

        if (_isAlive)
        {
            _timeAlive += msSinceLastFrame / 1000.0; // Accumulate time alive

            // Player input and movement
            double up = _input.IsUpPressed() ? 1.0 : 0.0;
            double down = _input.IsDownPressed() ? 1.0 : 0.0;
            double left = _input.IsLeftPressed() ? 1.0 : 0.0;
            double right = _input.IsRightPressed() ? 1.0 : 0.0;
            bool tryDropWater = _input.IsKeyBPressed();
            bool isAttacking = _input.IsKeyAPressed();


            // Calculate world boundaries for player clamping
            int worldPixelMinX = 0;
            int worldPixelMinY = 0;
            int worldPixelMaxX = _currentLevel.Width!.Value * _currentLevel.TileWidth!.Value;
            int worldPixelMaxY = _currentLevel.Height!.Value * _currentLevel.TileHeight!.Value;

            _player.UpdatePosition(up, down, left, right,
                                   worldPixelMinX, worldPixelMinY,
                                   worldPixelMaxX, worldPixelMaxY,
                                   msSinceLastFrame);

            if (isAttacking) // Handle attack input
            {
                _player.Attack();
            }

            _scriptEngine.ExecuteAll(this); // Scripts might spawn game bombs

            if (tryDropWater) // Player tries to drop water with B key
            {
                if ((DateTimeOffset.Now - _lastWaterDropTime).TotalSeconds >= WATER_DROP_COOLDOWN_SECONDS)
                {
                    // Water droplet by 'B' key: spreadsFire = false, translateCoordinates = false (use player's current pos)
                    AddBomb(_player.Position.X, _player.Position.Y, false, false);
                    _lastWaterDropTime = DateTimeOffset.Now;
                }
            }
            CheckPlayerOnFire(); // Check if player stepped on fire
        }
        else
        {
            // Game is over, player is not alive.
            // Handle input for restarting, e.g., if (Input.IsKeyJustPressed(KeyCode.R)) SetupWorld();
        }
    }

    public void RenderFrame()
    {
        _renderer.SetDrawColor(0, 0, 0, 255);
        _renderer.ClearScreen();

        var playerPosition = _player!.Position;
        _renderer.CameraLookAt(playerPosition.X, playerPosition.Y);

        RenderTerrain();
        RenderAllObjects();

        _renderer.PresentFrame();
    }

    public void RenderAllObjects()
    {
        var toRemove = new List<int>();
        var toAdd = new List<GameObject>(); //list which holds new game obj

        foreach (var gameObject in GetRenderables())
        {
            gameObject.Render(_renderer);
            if (gameObject is TemporaryGameObject tempGameObject && tempGameObject.IsExpired)
            {
                toRemove.Add(tempGameObject.Id);

                if (tempGameObject.SpreadsFire) // Fire Bomb
                {
                    HandleTerrainInteraction(tempGameObject); // Spreads fire
                }
                else
                {
                    HandleTerrainInteraction(tempGameObject); // Extinguishes fire

                    // Spawn the visual splash effect
                    try
                    {
                        SpriteSheet splashSheet = SpriteSheet.Load(_renderer, "WaterSplash.json", "Assets");
                        splashSheet.ActivateAnimation("ImpactSplash");
                        double splashVisualTtl = 999999;

                        TemporaryGameObject splashVisual = new TemporaryGameObject(
                            splashSheet,
                            splashVisualTtl,
                            tempGameObject.Position, // Spawn splash at the same position as the droplet
                            false
                        );
                        toAdd.Add(splashVisual); // Add to temporary list
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error creating splash effect: {ex.Message}");
                    }
                }
            }
        }

        // Remove expired objects
        foreach (var id in toRemove)
        {
            _gameObjects.Remove(id, out _);
        }
        // Add newly created objects (splash visual)
        foreach (var newObject in toAdd)
        {
            if (!_gameObjects.ContainsKey(newObject.Id))
            {
                _gameObjects.Add(newObject.Id, newObject);
            }
        }

        _player?.Render(_renderer);
    }

    public void RenderTerrain()
    {
        foreach (var currentLayer in _currentLevel.Layers)
        {
            for (int i = 0; i < _currentLevel.Width; ++i)
            {
                for (int j = 0; j < _currentLevel.Height; ++j)
                {
                    int? dataIndex = j * currentLayer.Width + i;
                    if (dataIndex == null)
                    {
                        continue;
                    }

                    var currentTileId = currentLayer.Data[dataIndex.Value] - 1;
                    if (currentTileId == null)
                    {
                        continue;
                    }

                    var currentTile = _tileIdMap[currentTileId.Value];

                    var tileWidth = currentTile.ImageWidth ?? 0;
                    var tileHeight = currentTile.ImageHeight ?? 0;

                    var sourceRect = new Rectangle<int>(0, 0, tileWidth, tileHeight);
                    var destRect = new Rectangle<int>(i * tileWidth, j * tileHeight, tileWidth, tileHeight);
                    _renderer.RenderTexture(currentTile.TextureId, sourceRect, destRect);
                }
            }
        }
    }

    public IEnumerable<RenderableGameObject> GetRenderables()
    {
        foreach (var gameObject in _gameObjects.Values)
        {
            if (gameObject is RenderableGameObject renderableGameObject)
            {
                yield return renderableGameObject;
            }
        }
    }

    public (int X, int Y) GetPlayerPosition()
    {
        return _player!.Position;
    }

    public void AddBomb(int X, int Y, bool spreadsFire, bool translateCoordinates = true)
    {
        var worldCoords = translateCoordinates ? _renderer.ToWorldCoordinates(X, Y) : new Vector2D<int>(X, Y);

        SpriteSheet spriteSheet;
        string animationToPlay;

        if (spreadsFire)
        {
            // bomb that spreads fire
            spriteSheet = SpriteSheet.Load(_renderer, "BombExploding.json", "Assets"); // Used existing bomb assets
            animationToPlay = "Explode";
        }
        else
        {
            // water droplet (spawned by left click/'B' button)
            spriteSheet = SpriteSheet.Load(_renderer, "WaterDroplet.json", "Assets"); // Added water droplet assets
            animationToPlay = "Falling";
        }
        spriteSheet.ActivateAnimation(animationToPlay);

        // Create TemporaryGameObject, passing the spreadsFire flag
        TemporaryGameObject bombObject = new(spriteSheet, 2.1, (worldCoords.X, worldCoords.Y), spreadsFire);
        _gameObjects.Add(bombObject.Id, bombObject);
    }

    private void HandleTerrainInteraction(TemporaryGameObject expiredObject)
    {
        if (_currentLevel.TileWidth == null || _currentLevel.TileHeight == null ||
            _currentLevel.Width == null || _currentLevel.Layers.Count <= WALKABLE_LAYER_INDEX) return;

        int tileWidth = _currentLevel.TileWidth.Value;
        int tileHeight = _currentLevel.TileHeight.Value;
        int levelWidthInTiles = _currentLevel.Width.Value;
        Layer groundLayer = _currentLevel.Layers[WALKABLE_LAYER_INDEX];

        if (groundLayer.Height == null || groundLayer.Data == null) return;

        int centerTileX = expiredObject.Position.X / tileWidth;
        int centerTileY = expiredObject.Position.Y / tileHeight;

        if (expiredObject.SpreadsFire)
        {
            int maxFireExtentY = 3; // Max vertical distance from center tile for the fire effect
            for (int offsetY = -maxFireExtentY; offsetY <= maxFireExtentY; ++offsetY)
            {
                int currentOffsetXLimit = 0;
                int absOffsetY = Math.Abs(offsetY);

                // Handling fire spread in a circle after bomb explosion
                if (absOffsetY == 3) currentOffsetXLimit = 1; // Top and bottom rows of fire (3 units wide)
                else if (absOffsetY == 2) currentOffsetXLimit = 2; // Next rows (5 units wide)
                else if (absOffsetY <= 1) currentOffsetXLimit = 3; // Center rows (7 units wide)

                for (int offsetX = -currentOffsetXLimit; offsetX <= currentOffsetXLimit; ++offsetX)
                {
                    int currentTileX = centerTileX + offsetX;
                    int currentTileY = centerTileY + offsetY;

                    // Check for the Tile coordinates
                    if (currentTileX < 0 || currentTileX >= levelWidthInTiles ||
                        currentTileY < 0 || currentTileY >= groundLayer.Height.Value)
                    {
                        continue;
                    }

                    int tileIndex = currentTileY * levelWidthInTiles + currentTileX;
                    if (tileIndex >= 0 && tileIndex < groundLayer.Data.Count)
                    {
                        groundLayer.Data[tileIndex] = BURNING_TILE_GID;
                    }
                }
            }
        }
        else // Logic for water droplet
        {
            int waterRadius = 1; // This creates a 3x3 square (center tile +/- 1)
            for (int offsetY = -waterRadius; offsetY <= waterRadius; ++offsetY)
            {
                for (int offsetX = -waterRadius; offsetX <= waterRadius; ++offsetX)
                {
                    int currentTileX = centerTileX + offsetX;
                    int currentTileY = centerTileY + offsetY;

                    // Bounds check for the tile coordinates
                    if (currentTileX < 0 || currentTileX >= levelWidthInTiles ||
                        currentTileY < 0 || currentTileY >= groundLayer.Height.Value)
                    {
                        continue;
                    }

                    int tileIndex = currentTileY * levelWidthInTiles + currentTileX;
                    if (tileIndex >= 0 && tileIndex < groundLayer.Data.Count)
                    {
                        if (groundLayer.Data[tileIndex] == BURNING_TILE_GID) // Only extinguish if it's burning ground
                        {
                            groundLayer.Data[tileIndex] = GRASS_TILE_GID; // Set tile back to normal
                        }
                    }
                }
            }
        }
    }

    private void CheckPlayerOnFire()
    {
        if (_player == null || _player.State.State == PlayerObject.PlayerState.GameOver) return;

        if (_currentLevel.TileWidth == null || _currentLevel.TileHeight == null ||
            _currentLevel.Width == null || _currentLevel.Layers.Count <= WALKABLE_LAYER_INDEX) return;

        int tileWidth = _currentLevel.TileWidth.Value;
        int tileHeight = _currentLevel.TileHeight.Value;
        int levelWidthInTiles = _currentLevel.Width.Value;
        Layer groundLayer = _currentLevel.Layers[WALKABLE_LAYER_INDEX];

        if (groundLayer.Height == null || groundLayer.Data == null || _player.SpriteSheet == null) return;

        int playerTileX = _player.Position.X / tileWidth;
        int playerTileY = _player.Position.Y / tileHeight;

        // Bounds check for player's tile
        if (playerTileX < 0 || playerTileX >= levelWidthInTiles ||
            playerTileY < 0 || playerTileY >= groundLayer.Height.Value)
        {
            return;
        }

        int playerTileIndex = playerTileY * levelWidthInTiles + playerTileX;

        if (playerTileIndex >= 0 && playerTileIndex < groundLayer.Data.Count)
        {
            if (groundLayer.Data[playerTileIndex] == BURNING_TILE_GID)
                _player.GameOver();
        }
    }

    public double GetTimeAlive()
    {
        return _timeAlive;
    }

}
