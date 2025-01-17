using System;
using System.Collections.Generic;
using Nez;
using Nez.Sprites;
using Nez.Textures;
using Nez.UI;
using BobGreenhands.Utils;
using BobGreenhands.Persistence;
using BobGreenhands.Map;
using BobGreenhands.Map.Tiles;
using BobGreenhands.Map.Items;
using BobGreenhands.Scenes.UIElements;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NLog;
using BobGreenhands.Map.MapObjects;
using System.Threading;
using BobGreenhands.Scenes.ECS.Entities;


namespace BobGreenhands.Scenes
{
    public class PlayScene : BaseScene, IInputProcessor
    {

        public enum LockedState
        {
            None,
            MapObjectLocked,
            ItemLocked
        }

        public static Savegame CurrentSavegame;

        public static Dictionary<TileType, Texture2D> TileTextures = new Dictionary<TileType, Texture2D>();
        public static Dictionary<ItemType, Texture2D> ItemTextures = new Dictionary<ItemType, Texture2D>();

        public static bool CamPosLocked;

        public const int SelectedMapObjectRenderLayer = 6;
        public const int MapObjectRenderLayer = 5;
        public const int BackgroundRenderLayer = 4;
        public const int SelectedTileRenderLayer = 3;
        public const int MapRenderLayer = 2;

        public const float RandomTickPercent = 0.001f;
        public const float GUIScale = 1f;

        public readonly SelectableArray<float> ZoomSteps = new SelectableArray<float>(new float[]{0f, 0.5f, 1f}, 0, false);

        private int ZoomStepsPointer = 0;

        public static Sprite SelectedTileSprite;
        public static Sprite LockedTileSprite;

        // We want Camera to move only in integer steps, for that to work,
        // we keep track of Camera's theoretical Position in float but when
        // actually setting Camera's position, we convert it to an integer.
        private float _camX, _camY = 0;
    
        private static LockedState _currentLockedState;
        public static LockedState CurrentLockedState
        {
            get
            {
                return _currentLockedState;
            }
            set
            {
                _currentLockedState = value;
                switch (value)
                {
                    case LockedState.None:
                        CamPosLocked = false;
                        _selectedTileSpriteRenderer.Sprite = SelectedTileSprite;
                        _selectedMapObjectRenderer.Sprite = SelectedTileSprite;
                        break;
                    case LockedState.ItemLocked:
                        CamPosLocked = false;
                        _selectedTileSpriteRenderer.Sprite = SelectedTileSprite;
                        _selectedMapObjectRenderer.Sprite = SelectedTileSprite;
                        break;
                    case LockedState.MapObjectLocked:
                        CamPosLocked = false;
                        _selectedTileSpriteRenderer.Sprite = SelectedTileSprite;
                        _selectedMapObjectRenderer.Sprite = LockedTileSprite;
                        break;
                    default:
                        break;
                }
            }
        }

        public static int LockedIndex;

        public static Inventory? LockedInventory;

        public static List<ISelectionBlocking> SelectionBlockingUIElements = new List<ISelectionBlocking>();

        public Hotbar Hotbar;

        public BalanceElement BalanceElement;

        public static InfoElement InfoElement;

        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private System.Threading.Tasks.Task _autoSave;

        private MapEntity _mapEntity;

        private Entity _selectedTileEntity;
        private static SpriteRenderer _selectedTileSpriteRenderer;

        private Entity _selectedMapObjectEntity;
        private static SpriteRenderer _selectedMapObjectRenderer;

        private Point _selectedTilePoint = Point.Zero;

        private readonly float _maxCamSpeed = 6f * (Game.TextureResolution / 32f);
        private float _horizontalCamMovement;
        private float _verticalCamMovement;
        private float _maxCameraXPos;
        private float _maxCameraYPos;

        private List<MapObject> _mapObjects = new List<MapObject>();

        private Bob Bob;

        private System.Random randomTick = new System.Random();

        public PlayScene(Savegame savegame)
        {
            if(Game.GameFolder.Settings.GetBool("vignette"))
            {
                VignettePostProcessor vignette = new VignettePostProcessor(0);
                vignette.Power = 0.5f;
                AddPostProcessor(vignette);
            }

            CurrentSavegame = savegame;
            savegame.Load();
            Game.SubscribeToInputHandler(this);

            // load all the textures
            // TODO: Make this somehow better
            SelectedTileSprite = new Sprite(FNATextureHelper.Load("img/ui/normal/selected_tile", Game.Content));
            LockedTileSprite = new Sprite(FNATextureHelper.Load("img/ui/normal/locked_tile", Game.Content));
            TileTextures[TileType.Unknown] = FNATextureHelper.Load("img/tiles/unknown", Game.Content);
            TileTextures[TileType.Grass] = FNATextureHelper.Load("img/tiles/grass", Game.Content);
            TileTextures[TileType.Farmland] = FNATextureHelper.Load("img/tiles/farmland", Game.Content);
            TileTextures[TileType.NSFence] = FNATextureHelper.Load("img/tiles/boundary_bush", Game.Content);
            TileTextures[TileType.WEFence] = FNATextureHelper.Load("img/tiles/boundary_bush", Game.Content);
            TileTextures[TileType.NEFence] = FNATextureHelper.Load("img/tiles/boundary_bush", Game.Content);
            TileTextures[TileType.ESFence] = FNATextureHelper.Load("img/tiles/boundary_bush", Game.Content);
            TileTextures[TileType.SWFence] = FNATextureHelper.Load("img/tiles/boundary_bush", Game.Content);
            TileTextures[TileType.WNFence] = FNATextureHelper.Load("img/tiles/boundary_bush", Game.Content);
            TileTextures[TileType.BoundaryBush] = FNATextureHelper.Load("img/tiles/boundary_bush", Game.Content);
            ItemTextures[ItemType.Unknown] = FNATextureHelper.Load("img/tiles/unknown", Game.Content);
            ItemTextures[ItemType.Shovel] = FNATextureHelper.Load("img/items/shovel", Game.Content);
            ItemTextures[ItemType.StrawberrySeeds] = FNATextureHelper.Load("img/items/strawberry_seeds", Game.Content);
            ItemTextures[ItemType.WateringCan] = FNATextureHelper.Load("img/items/watering_can", Game.Content);
            ItemTextures[ItemType.BigWateringCan] = FNATextureHelper.Load("img/items/big_watering_can", Game.Content);

            // set up the background
            Entity backgroundEntity = CreateEntity("background");
            RenderLayerRenderer backgroundLayerRenderer = new RenderLayerRenderer(0, BackgroundRenderLayer);
            AddRenderer(backgroundLayerRenderer);
            TiledSpriteRenderer tsr = new TiledSpriteRenderer(TileTextures[TileType.Grass]);
            tsr.SetRenderLayer(BackgroundRenderLayer);
            tsr.Height = 65536;
            tsr.Width = 65536;
            backgroundEntity.SetPosition(-tsr.Width/2f, -tsr.Height/2f);
            backgroundEntity.AddComponent(tsr);
            

            // build the map texture for the first time
            RenderLayerRenderer mapLayerRenderer = new RenderLayerRenderer(1, MapRenderLayer);
            AddRenderer(mapLayerRenderer);
            _mapEntity = new MapEntity();
            _mapEntity.SetPosition(-_mapEntity.Width/2f, -_mapEntity.Height/2f);
            AddEntity(_mapEntity);
            // TODO: Make zoom adjust to TextureResolution properly
            Camera.SetZoom(ZoomSteps.Get());
            RecalculateMaxCamPos();

            // init the selected tile sprite
            _selectedTileEntity = CreateEntity("selectedTile");
            RenderLayerRenderer selectedTileLayerRenderer = new RenderLayerRenderer(2, SelectedTileRenderLayer);
            AddRenderer(selectedTileLayerRenderer);
            _selectedTileSpriteRenderer = new SpriteRenderer(SelectedTileSprite);
            _selectedTileSpriteRenderer.SetRenderLayer(SelectedTileRenderLayer);
            _selectedTileEntity.AddComponent(_selectedTileSpriteRenderer);

            // mapobjects!
            foreach(MapObject m in CurrentSavegame.SavegameData.MapObjectList)
            {
                _mapObjects.Add(m);
                m.Initialize();
                AddEntity(m);
            }
            RenderLayerRenderer mapObjectLayerRenderer = new RenderLayerRenderer(3, MapObjectRenderLayer);
            AddRenderer(mapObjectLayerRenderer);
            Bob = new Bob(CurrentSavegame.SavegameData.PlayerXPos, CurrentSavegame.SavegameData.PlayerYPos);
            _mapObjects.Add(Bob);
            AddEntity(Bob);
            Camera.SetPosition(Bob.Position);

            _selectedMapObjectEntity = CreateEntity("selectedMapObject");
            RenderLayerRenderer selectedMapObjectRenderer = new RenderLayerRenderer(4, SelectedMapObjectRenderLayer);
            AddRenderer(selectedMapObjectRenderer);
            _selectedMapObjectRenderer = new SpriteRenderer(SelectedTileSprite);
            _selectedMapObjectRenderer.SetRenderLayer(SelectedMapObjectRenderLayer);
            _selectedMapObjectEntity.AddComponent(_selectedMapObjectRenderer);

            // ui stuff
            UICanvas.RenderLayer = BaseScene.UIRenderLayer;
            Table table = UICanvas.Stage.AddElement(new Table());
            table.SetFillParent(true);
            table.Pad(20);
            
            //table.Add(new TextButton(Language.Translate("menu"), Game.NormalSkin)).Expand().Top().Right();
            InfoElement = new InfoElement(FNATextureHelper.Load("img/ui/normal/info_element", Game.Content));
            table.Add(InfoElement).Expand().Top().Left();
            InfoElement.SetVisible(false);
            SelectionBlockingUIElements.Add(InfoElement);
            table.Row();

            Hotbar = new Hotbar();
            SelectionBlockingUIElements.Add(Hotbar);
            table.Add(Hotbar).Left();

            BalanceElement = new BalanceElement(CurrentSavegame.SavegameData.Balance);
            SelectionBlockingUIElements.Add(BalanceElement);
            table.Add(BalanceElement).Right().Bottom().SetExpandX();

            UpdateSelectedThing();

            // save the game every 10 secs
            _autoSave = new System.Threading.Tasks.Task(() =>
            {
                {
                    while (true)
                    {
                        CurrentSavegame.Save();
                        _log.Debug("Saved the game (Auto-Save)");
                        Thread.Sleep(10000);
                    }
                }
            });
            _autoSave.Start();
        }

        ~PlayScene()
        {
            _autoSave.Dispose();
        }

        // TODO: Improve this.
        private void RecalculateMaxCamPos()
        {
            _maxCameraXPos = Screen.Width / 4;
            _maxCameraYPos = Screen.Height / 4;
        }

        /// <summary>
        /// Return the MapObject that's below the mouse cursor
        /// </summary>
        private MapObject? GetBlockingMapObject()
        {
            foreach (MapObject m in _mapObjects)
            {
                SpriteRenderer spriteRenderer = m.SpriteRenderer;
                float minX = m.Position.X - spriteRenderer.Origin.X;
                float maxX = m.Position.X + (spriteRenderer.Width - spriteRenderer.Origin.X);
                float minY = m.Position.Y - spriteRenderer.Origin.Y;
                float maxY = m.Position.Y + (spriteRenderer.Height - spriteRenderer.Origin.Y);
                if(Camera.MouseToWorldPoint().X > minX && Camera.MouseToWorldPoint().X < maxX && Camera.MouseToWorldPoint().Y > minY && Camera.MouseToWorldPoint().Y < maxY)
                    return m;
            }
            return null;
        }

        /// <summary>
        /// Find out, if an UI-blocking entity is in the way
        /// </summary>
        private bool UIIsBlocking()
        {
            foreach (ISelectionBlocking i in SelectionBlockingUIElements)
            {
                if (i.HoveredOver())
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Marks a Tile or MapObject as marked or locked, depending on what the cursor is hovering on.
        /// </summary>
        private void UpdateSelectedThing()
        {
            // if there's an UI element blocking, disable any selection.
            if(UIIsBlocking())
            {
                _selectedTileEntity.Enabled = false;
                _selectedMapObjectEntity.Enabled = false;
                return;
            }
            MapObject? blockingMapEntity = GetBlockingMapObject();
            if (blockingMapEntity != null)
            {
                _selectedTileEntity.Enabled = false;
                SpriteRenderer spriteRenderer = blockingMapEntity.SpriteRenderer;
                float X = (blockingMapEntity.Position.X - spriteRenderer.Origin.X) + spriteRenderer.Width / 2;
                float Y = (blockingMapEntity.Position.Y - spriteRenderer.Origin.Y) + spriteRenderer.Height / 2;
                _selectedMapObjectEntity.SetScale(1f);
                Vector2 scale = new Vector2(spriteRenderer.Width / _selectedMapObjectRenderer.Width, spriteRenderer.Height / _selectedMapObjectRenderer.Height);
                _selectedMapObjectEntity.SetScale(scale);
                _selectedMapObjectEntity.Enabled = true;
                _selectedMapObjectEntity.SetPosition(new Vector2(X, Y));
                InfoElement.SetImage(spriteRenderer.Sprite);
                InfoElement.SetText(blockingMapEntity.GetInfoText());
                InfoElement.SetVisible(true);
                return;
            }
            else
            {
                _selectedTileEntity.Enabled = true;
                _selectedMapObjectEntity.Enabled = false;
                int xMapPos = (int)Math.Floor(Camera.MouseToWorldPoint().X / Game.TextureResolution) * Game.TextureResolution;
                int yMapPos = (int)Math.Floor(Camera.MouseToWorldPoint().Y / Game.TextureResolution) * Game.TextureResolution;

                _selectedTilePoint.X = xMapPos / Game.TextureResolution + (int)Math.Floor(CurrentSavegame.SavegameData.MapWidth / 2f);
                _selectedTilePoint.Y = yMapPos / Game.TextureResolution + (int)Math.Floor(CurrentSavegame.SavegameData.MapHeight / 2f);

                if(_selectedTilePoint.X < 0)
                {
                    _selectedTileEntity.Enabled = false;
                }
                else if(_selectedTilePoint.X > CurrentSavegame.SavegameData.MapWidth - 1)
                {
                    _selectedTileEntity.Enabled = false;
                }

                if (_selectedTilePoint.Y < 0)
                {
                    _selectedTileEntity.Enabled = false;
                }
                else if (_selectedTilePoint.Y > CurrentSavegame.SavegameData.MapHeight - 1)
                {
                    _selectedTileEntity.Enabled = false;
                }

                _selectedTileEntity.SetPosition(xMapPos + Game.TextureResolution/2, yMapPos + Game.TextureResolution/2);
                InfoElement.SetVisible(false);
            }

        }

        /// <summary>
        /// Sets a given InventoryItem as locked.
        /// </summary>
        public static void SetLockedItem(InventoryItem? item)
        {
            if (item == null)
            {
                if (LockedInventory != null)
                    LockedInventory.GetItemAt(LockedIndex).Locked.SetVisible(false);
                CurrentLockedState = LockedState.None;
                LockedInventory = null;
                LockedIndex = -1;
            }
            else
            {
                CurrentLockedState = LockedState.ItemLocked;
                LockedInventory = item.Inventory;
                LockedIndex = item.Index;
                item.Locked.SetVisible(true);
            }
        }

        /// <summary>
        /// Checks whether the camera should be moving due to the position of the mouse and moves it accordingly.
        /// </summary>
        public void MoveCamera()
        {
            float newXPos = Math.Clamp(_camX - _horizontalCamMovement * (Time.DeltaTime * 60), -_maxCameraXPos, _maxCameraXPos);
            float newYPos = Math.Clamp(_camY - _verticalCamMovement * (Time.DeltaTime * 60), -_maxCameraYPos, _maxCameraYPos);
            if(!CamPosLocked) {
                _camX = newXPos;
                _camY = newYPos;
                if(newXPos != Camera.Position.X || newYPos != Camera.Position.Y)
                {
                    Camera.SetPosition(new Vector2((int) newXPos, (int) newYPos));
                }
            }
        }

        /// <summary>
        /// Random ticks random MapEntities; amount depending on the frametime.
        /// </summary>
        public void GlobalRandomTick()
        {
            List<MapObject> toBeRandomTicked = new List<MapObject>();
            foreach (MapObject m in _mapObjects.ToArray())
            {
                // random tick
                // since tick rate is tied to the FPS, we have to figure out a way, to still have a reasonable random tick rate no matter the fps
                // we say, that with 60 FPS we want ``RandomTickPercent`` of the on-screen entities random ticked
                // since any computer built in this millennium is able to surpass that easily, we have to compensate for that
                if(randomTick.NextDouble() < RandomTickPercent * (Time.DeltaTime * 60f))
                {
                    toBeRandomTicked.Add(m);
                }
            }
            foreach (MapObject m in toBeRandomTicked)
            {
                m.OnRandomTick(Game.GameTime);
            }
        }

        /// <summary>
        /// Ticks all MapEntities.
        /// </summary>
        public void GlobalTick()
        {
            foreach (MapObject m in _mapObjects.ToArray())
            {
                m.OnTick(Game.GameTime);
            }
        }

        /// <summary>
        /// Forces the MapEntity to refresh.
        /// </summary>
        public void RefreshMap()
        {
            _mapEntity.Refresh();
        }

        /// <summary>
        /// Returns a list of all MapObjects.
        /// </summary>
        public List<MapObject> GetMapObjects()
        {
            return _mapObjects;
        }

        /// <summary>
        /// Adds a MapObject to the scene.
        /// </summary>
        public void AddMapObject(MapObject mapObject)
        {
            List<MapObject> mapObjects = _mapObjects;
            mapObjects.Add(mapObject);
            _mapObjects = mapObjects;
            AddEntity(mapObject);
            CurrentSavegame.SavegameData.MapObjectList.Add(mapObject);
        }

        /// <summary>
        /// Removes a MapObject from the scene.
        /// </summary>
        public void DestroyMapObject(MapObject mapObject)
        {
            List<MapObject> mapObjects = _mapObjects;
            mapObjects.Remove(mapObject);
            _mapObjects = mapObjects;
            mapObject.Destroy();
            CurrentSavegame.SavegameData.MapObjectList.Remove(mapObject);
        }

        // TODO: make that a little bit more efficient
        /// <summary>
        /// Returns true when the cursor is hovering over a MapObject whose OccupiesTiles value is true.
        /// </summary>
        public bool IsOccupiedByMapObject(float x, float y)
        {
            foreach (MapObject m in _mapObjects.ToArray())
            {
                if(!m.OccupiesTiles)
                    continue;
                Location minLocation = Location.FromEntityCoordinates(m.Position.X - m.SpriteRenderer.Origin.X, m.Position.Y - m.SpriteRenderer.Origin.Y);
                Location maxLocation = Location.FromEntityCoordinates(m.Position.X + (m.Hitbox.X - m.SpriteRenderer.Origin.X), m.Position.Y + (m.Hitbox.Y - m.SpriteRenderer.Origin.Y));
                if(x >= minLocation.X && x < maxLocation.X && y >= minLocation.Y && y < maxLocation.Y)
                {
                    return true;
                }
            }
            return false;
        }

        public override void Update()
        {
            base.Update();

            MoveCamera();
        
            if(CurrentLockedState == LockedState.None || CurrentLockedState == LockedState.ItemLocked)
                UpdateSelectedThing();

            GlobalRandomTick();
            GlobalTick();
        }

        public void FirstExtendedMousePressed()
        {
        }

        public void FirstExtendedMouseReleased()
        {
        }

        public void KeyPressed(Keys key)
        {
        }

        public void KeyReleased(Keys key)
        {
            if (key == Keys.Z)
            {
                Bob.ResetTasks();
            }
            else if (key == Keys.S && Input.IsKeyDown(Keys.LeftControl))
            {
                CurrentSavegame.Save();
            }
            else if (key == Keys.F9)
            {
                Camera.Zoom = ZoomSteps.ModifyPointer(-1);
                RecalculateMaxCamPos();
                if (MathUtils.IsBetween(Camera.Position.X, -_maxCameraXPos, _maxCameraXPos) && MathUtils.IsBetween(Camera.Position.Y, -_maxCameraYPos, _maxCameraYPos))
                {
                    Camera.Position = Vector2.Zero;
                }

            }
            else if (key == Keys.F10)
            {
                Camera.Zoom = ZoomSteps.ModifyPointer(1);
                RecalculateMaxCamPos();
                if (MathUtils.IsBetween(Camera.Position.X, -_maxCameraXPos, _maxCameraXPos) && MathUtils.IsBetween(Camera.Position.Y, -_maxCameraYPos, _maxCameraYPos))
                {
                    Camera.Position = Vector2.Zero;
                }
            }
        }

        public void LeftMousePressed()
        {
        }

        public void LeftMouseReleased()
        {
            if(UIIsBlocking())
                return;
            switch(CurrentLockedState)
            {
                case LockedState.None:
                    if(GetBlockingMapObject() != null)
                        CurrentLockedState = LockedState.MapObjectLocked;
                    break;
                case LockedState.MapObjectLocked:
                    CurrentLockedState = LockedState.None;
                    break;
                case LockedState.ItemLocked:
                    Item item = LockedInventory.GetItemAt(LockedIndex).Item;
                    MapObject blockingMapObject = GetBlockingMapObject();
                    if(item == null)
                        return;
                    if(blockingMapObject == null) {
                        int x = _selectedTilePoint.X;
                        int y = _selectedTilePoint.Y;
                        // if mouse is out of bounds
                        if(x < 0 || x >= CurrentSavegame.SavegameData.MapWidth || y < 0 || y >= CurrentSavegame.SavegameData.MapWidth)
                            return;
                        TileType tileType = CurrentSavegame.GetTileAt(x, y);
                        Vector2 target = new Vector2((int) Camera.MouseToWorldPoint().X, (int) Camera.MouseToWorldPoint().Y);
                        Location location = Location.FromEntityCoordinates(target).SetToCenterOfTile();
                        Action function = () => {if(item.UsedOnTile(x, y, tileType, this)) { RefreshMap(); Hotbar.Refresh(); }};
                        Bob.EnqueueTask(new Task(location.EntityCoordinates, function));
                        Bob.IsMoving = true;
                    }
                    else
                    {
                        if(blockingMapObject == Bob)
                        {
                            item.UsedOnMapObject(blockingMapObject, this);
                        }
                        else
                        {
                            Action function = () => {item.UsedOnMapObject(blockingMapObject, this); Hotbar.Refresh();};
                            Bob.EnqueueTask(new Task(blockingMapObject.Position, function));
                            Bob.IsMoving = true;
                        }
                    }
                    break;
            }
        }

        public void MiddleMousePressed()
        {
        }

        public void MiddleMouseReleased()
        {
        }

        public void MouseMoved(Point delta)
        {
            int xPos = Input.RawMousePosition.X;
            int yPos = Input.RawMousePosition.Y;

            // check if cursor is out of the window or if a UI element is blocking
            if (xPos < 0 || xPos > Screen.Width || yPos < 0 || yPos > Screen.Height || UIIsBlocking())
                return;

            float horizontalThreshhold = Screen.Width / 20f;
            float verticalThreshhold = Screen.Height / 20f;

            _horizontalCamMovement = 0;
            _verticalCamMovement = 0;

            if (xPos < horizontalThreshhold)
                _horizontalCamMovement = (1 - (xPos / horizontalThreshhold)) * _maxCamSpeed;
            if (yPos < verticalThreshhold)
                _verticalCamMovement = (1 - (yPos / verticalThreshhold)) * _maxCamSpeed;
            if (xPos > Screen.Width - horizontalThreshhold)
                _horizontalCamMovement = -(1 - ((Screen.Width - xPos) / horizontalThreshhold)) * _maxCamSpeed;
            if (yPos > Screen.Height - verticalThreshhold)
                _verticalCamMovement = -(1 - ((Screen.Height - yPos) / verticalThreshhold)) * _maxCamSpeed;

            if(CurrentLockedState == LockedState.None || CurrentLockedState == LockedState.ItemLocked)
                UpdateSelectedThing();
        }

        public void MouseScrolled(int delta)
        {
        }

        public void RightMousePressed()
        {
        }

        public void RightMouseReleased()
        {
            if(CurrentLockedState == LockedState.None && !UIIsBlocking())
            {
                Vector2 destination = new Vector2((int) Camera.MouseToWorldPoint().X, (int) Camera.MouseToWorldPoint().Y);;
                Location location = Location.FromEntityCoordinates(destination);
                // if mouse is out of bounds
                if(location.X < 0 || location.X >= CurrentSavegame.SavegameData.MapWidth || location.Y < 0 || location.Y >= CurrentSavegame.SavegameData.MapWidth)
                    return;
                Bob.EnqueueTask(new Task(destination, null));
                Bob.IsMoving = true;
            }
        }

        public void ScaledMouseMoved(Vector2 delta)
        {
        }

        public void SecondExtendedMousePressed()
        {
        }

        public void SecondExtendedMouseReleased()
        {
        }
    }
}