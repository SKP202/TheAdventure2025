using System.Reflection;
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

    private bool _wasEPressedLastFrame = false;
    private bool _wasQPressedLastFrame = false;

    private DateTimeOffset _lastUpdate = DateTimeOffset.Now;

    private double _skeletonSpawnInterval = 10.0;
    private double _skeletonSpawnTimer = 0.0;
    private double _skeletonSpawnAcceleration = 0.99;
    private const double _skeletonSpawnMinInterval = 0.3;
    private Random _rng = new();

 
    private int _skeletonsKilled = 0;

    public Engine(GameRenderer renderer, Input input)
    {
        _renderer = renderer;
        _input = input;
    }

    public void SetupWorld()
    {
        _player = new(SpriteSheet.Load(_renderer, "Player.json", "Assets"), 100, 100);

        var levelContent = File.ReadAllText(Path.Combine("Assets", "terrain.tmj"));
        var level = JsonSerializer.Deserialize<Level>(levelContent);
        if (level == null)
        {
            throw new Exception("Failed to load level");
        }

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

   

    private void SpawnSkeleton()
    {
   
        int mapWidth = _currentLevel.Width!.Value * _currentLevel.TileWidth!.Value;
        int mapHeight = _currentLevel.Height!.Value * _currentLevel.TileHeight!.Value;

       
        int safeRadius = 64;
        int x, y;
        do
        {
            x = _rng.Next(0, mapWidth);
            y = _rng.Next(0, mapHeight);
        } while (_player != null && Math.Abs(x - _player.Position.X) < safeRadius && Math.Abs(y - _player.Position.Y) < safeRadius);

        var skeletonSheet = SpriteSheet.Load(_renderer, "skeleton.json", "Assets");
        skeletonSheet.ActivateAnimation("Walk");
        var skeleton = new SkeletonObject(skeletonSheet, (x, y));
        _gameObjects.Add(skeleton.Id, skeleton);
    }

    public void ProcessFrame()
    {
        var currentTime = DateTimeOffset.Now;
        var msSinceLastFrame = (currentTime - _lastUpdate).TotalMilliseconds;
        _lastUpdate = currentTime;

        if (_player == null)
        {
            return;
        }

        double up = _input.IsUpPressed() ? 1.0 : 0.0;
        double down = _input.IsDownPressed() ? 1.0 : 0.0;
        double left = _input.IsLeftPressed() ? 1.0 : 0.0;
        double right = _input.IsRightPressed() ? 1.0 : 0.0;
        bool isEPressed = _input.IsKeyEPressed();
        bool isQPressed = _input.IsKeyQPressed();

        _player.UpdatePosition(up, down, left, right, 48, 48, msSinceLastFrame);
        _scriptEngine.ExecuteAll(this);

      
        _skeletonSpawnTimer += msSinceLastFrame / 1000.0;
        while (_skeletonSpawnTimer >= _skeletonSpawnInterval)
        {
            SpawnSkeleton();
            _skeletonSpawnTimer -= _skeletonSpawnInterval;
            _skeletonSpawnInterval = Math.Max(_skeletonSpawnInterval * _skeletonSpawnAcceleration, _skeletonSpawnMinInterval);
        }

      
        foreach (var obj in _gameObjects.Values)
        {
            if (obj is SkeletonObject skeleton)
            {
                skeleton.Update(_player, msSinceLastFrame);
            }
        }

        
        if (isEPressed && !_wasEPressedLastFrame)
        {
            AddBomb(_player.Position.X, _player.Position.Y, false);
        }

        
        if (isQPressed && !_wasQPressedLastFrame)
        {
            
            var playerPos = _player.Position;
            TemporaryGameObject? nearestBomb = null;
            double minDist = 32.0; 

            foreach (var obj in _gameObjects.Values)
            {
                if (obj is TemporaryGameObject bomb)
                {
                    double dx = bomb.Position.X - playerPos.X;
                    double dy = bomb.Position.Y - playerPos.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearestBomb = bomb;
                    }
                }
            }

            if (nearestBomb != null)
            {
              
                var dir = _player.State.Direction;
                int dx = 0, dy = 0;
                switch (dir)
                {
                    case PlayerObject.PlayerStateDirection.Up: dy = -48; break;
                    case PlayerObject.PlayerStateDirection.Down: dy = 48; break;
                    case PlayerObject.PlayerStateDirection.Left: dx = -48; break;
                    case PlayerObject.PlayerStateDirection.Right: dx = 48; break;
                }

                
                nearestBomb.Position = (nearestBomb.Position.X + dx, nearestBomb.Position.Y + dy);

               
                _player.Attack();
            }
        }

        _wasEPressedLastFrame = isEPressed; 
        _wasQPressedLastFrame = isQPressed;
    }

    public void RenderFrame()
    {
        _renderer.SetDrawColor(0, 0, 0, 255);
        _renderer.ClearScreen();

        var playerPosition = _player!.Position;
        _renderer.CameraLookAt(playerPosition.X, playerPosition.Y);

        RenderTerrain();
        RenderAllObjects();
        DrawPlayerHealthBar();
        DrawSkeletonKillCounter(); 

        _renderer.PresentFrame();
    }

    public void RenderAllObjects()
    {
        var toRemove = new List<int>();

       
        foreach (var obj in _gameObjects.Values)
        {
          
            if (obj is TemporaryGameObject bomb && bomb.IsExpired)
            {
                foreach (var target in _gameObjects.Values)
                {
                    if (target is SkeletonObject skeleton && !skeleton.IsDead)
                    {
                        double dx = bomb.Position.X - skeleton.Position.X;
                        double dy = bomb.Position.Y - skeleton.Position.Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        if (dist < 32)
                        {
                            skeleton.Kill();
                        }
                    }
                }
            }
        }

       
        foreach (var gameObject in GetRenderables())
        {
            gameObject.Render(_renderer);

            if (gameObject is TemporaryGameObject { IsExpired: true } tempGameObject)
            {
                toRemove.Add(tempGameObject.Id);
            }
            else if (gameObject is SkeletonObject skeleton && skeleton.ShouldRemove())
            {
                toRemove.Add(skeleton.Id);
            }
        }

        foreach (var id in toRemove)
        {
            _gameObjects.Remove(id, out var gameObject);

            if (_player == null)
            {
                continue;
            }

            if (gameObject is TemporaryGameObject tempGameObject)
            {
                var deltaX = Math.Abs(_player.Position.X - tempGameObject.Position.X);
                var deltaY = Math.Abs(_player.Position.Y - tempGameObject.Position.Y);
                if (deltaX < 32 && deltaY < 32)
                {
                    _player.TakeDamage(_player.MaxHealth / 2);
                }
            }
            else if (gameObject is SkeletonObject)
            {
                _skeletonsKilled++;
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

    public void AddBomb(int X, int Y, bool translateCoordinates = true)
    {
        var worldCoords = translateCoordinates ? _renderer.ToWorldCoordinates(X, Y) : new Vector2D<int>(X, Y);

        SpriteSheet spriteSheet = SpriteSheet.Load(_renderer, "BombExploding.json", "Assets");
        spriteSheet.ActivateAnimation("Explode");

        TemporaryGameObject bomb = new(spriteSheet, 2.1, (worldCoords.X, worldCoords.Y));
        _gameObjects.Add(bomb.Id, bomb);
    }

    private void DrawPlayerHealthBar()
    {
        if (_player == null)
            return;

        int barWidth = 60;
        int barHeight = 8;
        int outlineThickness = 2;
        int x = _player.Position.X - barWidth / 2;
        int y = _player.Position.Y - 40;

        float healthRatio = Math.Clamp(_player.Health / (float)_player.MaxHealth, 0f, 1f);
        int fillWidth = (int)(barWidth * healthRatio);

      
        _renderer.SetDrawColor(0, 0, 0, 255);
        _renderer.DrawFilledBar(x - outlineThickness, y - outlineThickness, barWidth + 2 * outlineThickness, barHeight + 2 * outlineThickness);

        
        _renderer.SetDrawColor(40, 40, 40, 255);
        _renderer.DrawFilledBar(x, y, barWidth, barHeight);

    
        _renderer.SetDrawColor(60, 220, 60, 255);
        _renderer.DrawFilledBar(x, y, fillWidth, barHeight);
    }

    private void DrawSkeletonKillCounter()
    {
        _renderer.SetWindowTitle($"Skeletons Killed: {_skeletonsKilled}");
    }
}