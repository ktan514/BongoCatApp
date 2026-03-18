using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SharpHook;
using System;
using System.Collections.Generic;

namespace BongoCatAPP;

public partial class MainWindow : Window
{
    private const string AssetsPath = "avares://BongoCatAPP/Assets/";

    private readonly List<CharacterDefinition> _characters = [];
    private CharacterDefinition? _currentCharacter;
    private readonly Dictionary<string, SpriteAssets> _spriteCache = [];

    private const int AnimationFps = 60;
    private const double WalkAnimFps = 8.0;
    private const double GrabAnimFps = 12.0;

    private const double WalkSpeedX = 120.0;   // px/s
    private const double WalkSpeedYScale = 0.5;

    private const float WheelShakeBaseCycles = 3.0f;
    private const float WheelShakeMinAmplitude = 0.0f;
    private const double WheelShakePhasePerSecond = 80.0;

    private const double DragThresholdX = 100;
    private const double DragThresholdY = 50;

    private readonly TaskPoolGlobalHook _hook;
    private readonly Random _random = new();

    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _tweetTimer;

    private SpriteAssets? _sprites = null;
    private readonly BehaviorState _behavior = new();
    private readonly WalkState _walk = new();
    private readonly WheelShakeState _wheelShake = new();
    private readonly AnimationState _animation = new();

    private readonly string[] _dragMessages =
    [
        "つかまったー！",
        "はなしてほしいな…",
        "ゆらゆらする！",
        "どこいくの？",
        "わーっ",
        "びよーん",
        "運ばれてる…",
        "つままれ中！",
        "空を飛んでる気分",
        "これはこれでありかも？"
    ];

    private readonly string[] _idleMessages =
    [
        "ひまだなー",
        "なにかしようよ",
        "待機中だよ",
        "ねむくなってきた…",
        "見てるよー",
        "タイピングしてほしいな",
        "しーん…",
        "今日は何するの？",
        "ちょこん"
    ];

    public MainWindow()
    {
        InitializeComponent();
        InitializeCharacters();
        SwitchCharacter("rabbit");

        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / AnimationFps)
        };
        _animationTimer.Tick += (_, _) => UpdateAnimationFrame();
        _animationTimer.Start();

        _idleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _idleTimer.Tick += (_, _) => CheckIdle();
        _idleTimer.Start();

        _tweetTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _tweetTimer.Tick += (_, _) => ShowRandomIdleMessage();

        _hook = CreateHook();
        _hook.RunAsync();
    }

    private sealed class SpriteAssets
    {
        public required Bitmap Idle { get; init; }
        public required Bitmap[] ActionImages { get; init; }
        public required Bitmap[] GrabImages { get; init; }
        public required Bitmap[] WalkImages { get; init; }

        public TranslateTransform ShakeTransform { get; } = new(0, 0);
        public ScaleTransform FacingTransform { get; } = new(1, 1);
        public TransformGroup TransformGroup { get; } = new();
    }

    private sealed class BehaviorState
    {
        public int Count { get; set; }
        public bool IsDragging { get; set; }
        public bool IsGrabCursor { get; set; }
        public bool IsTweeting { get; set; }
        public bool IsWalking { get; set; }
        public bool IsNextLeftAction { get; set; } = true;
        public DateTime LastInputAt { get; set; } = DateTime.Now;
    }

    private sealed class WalkState
    {
        public int DirectionX { get; set; } = 12;
        public int DirectionY { get; set; } = 0;
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public int FrameIndex { get; set; }
        public double FrameElapsed { get; set; }
    }

    private sealed class WheelShakeState
    {
        public float RemainingCycles { get; set; }
        public int Phase { get; set; }
        public float InitialCycles { get; set; }
        public float CurrentAmplitude { get; set; } = 3.0f;
        public double Elapsed { get; set; }
    }

    private sealed class AnimationState
    {
        public DateTime LastFrameAt { get; set; } = DateTime.UtcNow;
        public int GrabFrameIndex { get; set; }
        public double GrabFrameElapsed { get; set; }
    }

    private SpriteAssets LoadSprites(CharacterDefinition character)
    {
        if (_spriteCache.TryGetValue(character.Id, out var cached))
        {
            return cached;
        }

        var sprites = new SpriteAssets
        {
            Idle = LoadImage(character.IdleFile),
            ActionImages =
            [
                LoadImage(character.ActionLeftFile),
                LoadImage(character.ActionRightFile)
            ],
            GrabImages =
            [
                LoadImage(character.Grab1File),
                LoadImage(character.Grab2File)
            ],
            WalkImages =
            [
                LoadImage(character.Walk1File),
                LoadImage(character.Walk2File)
            ]
        };

        sprites.TransformGroup.Children.Add(sprites.FacingTransform);
        sprites.TransformGroup.Children.Add(sprites.ShakeTransform);

        _spriteCache[character.Id] = sprites;
        return sprites;
    }

    private static Bitmap LoadImage(string fileName)
    {
        return new Bitmap(AssetLoader.Open(new Uri(AssetsPath + fileName)));
    }

    private TaskPoolGlobalHook CreateHook()
    {
        var hook = new TaskPoolGlobalHook();

        hook.KeyPressed += (_, _) =>
        {
            OnUserInput();
            HandleKeyboardPressed();
        };

        hook.KeyReleased += (_, _) =>
        {
            if (_behavior.IsDragging)
            {
                return;
            }

            Dispatcher.UIThread.Post(SetIdlePose);
        };

        hook.MousePressed += (_, e) =>
        {
            OnUserInput();
            HandleMousePressed(e);
        };

        hook.MouseReleased += (_, _) =>
        {
            if (_behavior.IsDragging)
            {
                return;
            }

            Dispatcher.UIThread.Post(SetIdlePose);
        };

        hook.MouseWheel += (_, e) =>
        {
            OnUserInput();

            Dispatcher.UIThread.Post(() =>
            {
                AddWheelShake(e.Data.Rotation);
            });
        };

        return hook;
    }

    private void OnUserInput()
    {
        _behavior.LastInputAt = DateTime.Now;
        Dispatcher.UIThread.Post(StopIdleBehaviors);
    }

    private void HandleKeyboardPressed()
    {
        if (_behavior.IsDragging)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _behavior.Count++;
            UpdateCounterText();

            int actionIndex = _behavior.IsNextLeftAction ? 0 : 1;
            _behavior.IsNextLeftAction = !_behavior.IsNextLeftAction;

            UpdateCatImage(_sprites!.ActionImages[actionIndex]);
        });
    }

    private void HandleMousePressed(MouseHookEventArgs e)
    {
        if (_behavior.IsDragging)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            switch (e.Data.Button)
            {
                case SharpHook.Data.MouseButton.Button1:
                    _behavior.Count++;
                    UpdateCounterText();
                    UpdateCatImage(_sprites!.ActionImages[0]);
                    break;

                case SharpHook.Data.MouseButton.Button2:
                    _behavior.Count++;
                    UpdateCounterText();
                    UpdateCatImage(_sprites!.ActionImages[1]);
                    break;
            }
        });
    }

    private void CheckIdle()
    {
        if (_behavior.IsDragging)
        {
            return;
        }

        var idleSeconds = (DateTime.Now - _behavior.LastInputAt).TotalSeconds;

        if (idleSeconds >= 60)
        {
            if (!_behavior.IsWalking)
            {
                StartWalking();
            }
        }

        if (idleSeconds >= 15)
        {
            if (!_behavior.IsTweeting)
            {
                StartTweeting();
            }
        }
    }

    private void StartTweeting()
    {
        if (_behavior.IsTweeting)
        {
            return;
        }

        _behavior.IsTweeting = true;
        ShowRandomIdleMessage();
        _tweetTimer.Start();
    }

    private void StopTweeting()
    {
        if (!_behavior.IsTweeting)
        {
            return;
        }

        _behavior.IsTweeting = false;
        _tweetTimer.Stop();
        UpdateContextMessage(string.Empty);
    }

    private void ShowRandomIdleMessage()
    {
        if (!_behavior.IsTweeting || _idleMessages.Length == 0)
        {
            return;
        }

        string message = _idleMessages[_random.Next(_idleMessages.Length)];
        UpdateContextMessage(message);
    }

    private void ShowRandomDragMessage()
    {
        if (_dragMessages.Length == 0)
        {
            return;
        }

        string message = _dragMessages[_random.Next(_dragMessages.Length)];
        UpdateContextMessage(message);
    }

    private void StartWalking()
    {
        if (_behavior.IsWalking)
        {
            return;
        }

        _behavior.IsWalking = true;
        _walk.PositionX = Position.X;
        _walk.PositionY = Position.Y;
        _walk.FrameElapsed = 0.0;
        _walk.FrameIndex = 0;

        RandomizeWalkDirection();
        UpdateContextMessage(string.Empty);
    }

    private void StopWalking()
    {
        if (!_behavior.IsWalking)
        {
            return;
        }

        _behavior.IsWalking = false;
        _walk.FrameElapsed = 0.0;
        _walk.FrameIndex = 0;
        ApplyFacing(isRightFacing: false);
    }

    private void StopIdleBehaviors()
    {
        StopTweeting();
        StopWalking();

        if (!_behavior.IsDragging)
        {
            UpdateCounterText();
        }
    }

    private void RandomizeWalkDirection()
    {
        int[] speeds = [2, 4, 5, 7];

        int dx;
        int dy;

        do
        {
            dx = speeds[_random.Next(speeds.Length)] * (_random.Next(0, 2) == 0 ? -1 : 1);
            dy = speeds[_random.Next(speeds.Length)] * (_random.Next(0, 2) == 0 ? -1 : 1);
            dy /= 2;
        }
        while (dx == 0 && dy == 0);

        _walk.DirectionX = dx;
        _walk.DirectionY = dy;

        SetWalkFacingDirection();
    }

    private void AddWheelShake(double rotation)
    {
        _wheelShake.RemainingCycles = WheelShakeBaseCycles;
        _wheelShake.InitialCycles = WheelShakeBaseCycles;
        _wheelShake.CurrentAmplitude = Math.Max(3.0f, (float)Math.Min(5.0, Math.Abs(rotation) * 2.0));
    }

    private float CalculateWheelShakeAmplitude()
    {
        if (_wheelShake.InitialCycles <= 0)
        {
            return WheelShakeMinAmplitude;
        }

        float ratio = _wheelShake.RemainingCycles / _wheelShake.InitialCycles;
        float amplitude = (float)Math.Round(_wheelShake.CurrentAmplitude * ratio);

        if (amplitude < WheelShakeMinAmplitude)
        {
            amplitude = WheelShakeMinAmplitude;
        }

        return amplitude;
    }

    private void UpdateAnimationFrame()
    {
        var now = DateTime.UtcNow;
        double deltaTime = (now - _animation.LastFrameAt).TotalSeconds;
        _animation.LastFrameAt = now;

        if (deltaTime > 0.1)
        {
            deltaTime = 0.1;
        }

        UpdateWalkMovement(deltaTime);
        UpdateSpriteAnimation(deltaTime);
        UpdateWheelShakeVisual(deltaTime);
    }

    private void UpdateWalkMovement(double deltaTime)
    {
        if (!_behavior.IsWalking || _behavior.IsDragging)
        {
            return;
        }

        _walk.PositionX += _walk.DirectionX * WalkSpeedX * deltaTime / 12.0;
        _walk.PositionY += _walk.DirectionY * WalkSpeedX * WalkSpeedYScale * deltaTime / 12.0;

        int newX = (int)Math.Round(_walk.PositionX);
        int newY = (int)Math.Round(_walk.PositionY);

        var screen = Screens.Primary;
        if (screen is null)
        {
            Position = new PixelPoint(newX, newY);
            return;
        }

        var area = screen.WorkingArea;
        int windowWidth = (int)Bounds.Width;
        int windowHeight = (int)Bounds.Height;

        int minX = area.X;
        int minY = area.Y;
        int maxX = area.X + area.Width - windowWidth;
        int maxY = area.Y + area.Height - windowHeight;

        if (newX <= minX)
        {
            newX = minX;
            _walk.PositionX = newX;
            _walk.DirectionX = Math.Abs(_walk.DirectionX);
            SetWalkFacingDirection();
        }
        else if (newX >= maxX)
        {
            newX = maxX;
            _walk.PositionX = newX;
            _walk.DirectionX = -Math.Abs(_walk.DirectionX);
            SetWalkFacingDirection();
        }

        if (newY <= minY)
        {
            newY = minY;
            _walk.PositionY = newY;
            _walk.DirectionY = Math.Abs(_walk.DirectionY);
        }
        else if (newY >= maxY)
        {
            newY = maxY;
            _walk.PositionY = newY;
            _walk.DirectionY = -Math.Abs(_walk.DirectionY);
        }

        Position = new PixelPoint(newX, newY);
    }

    private void UpdateSpriteAnimation(double deltaTime)
    {
        if (_behavior.IsDragging)
        {
            _animation.GrabFrameElapsed += deltaTime;
            double frameInterval = 1.0 / GrabAnimFps;

            while (_animation.GrabFrameElapsed >= frameInterval)
            {
                _animation.GrabFrameElapsed -= frameInterval;
                _animation.GrabFrameIndex =
                    (_animation.GrabFrameIndex + 1) % _sprites!.GrabImages.Length;
            }

            UpdateCatImage(_sprites!.GrabImages[_animation.GrabFrameIndex]);
            return;
        }

        if (_behavior.IsWalking)
        {
            _walk.FrameElapsed += deltaTime;
            double frameInterval = 1.0 / WalkAnimFps;

            while (_walk.FrameElapsed >= frameInterval)
            {
                _walk.FrameElapsed -= frameInterval;
                _walk.FrameIndex = (_walk.FrameIndex + 1) % _sprites!.WalkImages.Length;
            }

            SetWalkFacingDirection();
            UpdateCatImage(_sprites!.WalkImages[_walk.FrameIndex]);
        }
    }

    private void UpdateWheelShakeVisual(double deltaTime)
    {
        if (_wheelShake.RemainingCycles <= 0 && _wheelShake.Phase == 0)
        {
            _sprites!.ShakeTransform.X = 0;
            _sprites!.ShakeTransform.Y = 0;
            return;
        }

        _wheelShake.Elapsed += deltaTime;
        double phaseInterval = 1.0 / WheelShakePhasePerSecond;

        while (_wheelShake.Elapsed >= phaseInterval)
        {
            _wheelShake.Elapsed -= phaseInterval;
            float amplitude = CalculateWheelShakeAmplitude();

            switch (_wheelShake.Phase)
            {
                case 0:
                    _sprites!.ShakeTransform.Y = -amplitude;
                    _wheelShake.Phase = 1;
                    break;

                case 1:
                    _sprites!.ShakeTransform.Y = amplitude;
                    _wheelShake.Phase = 2;
                    break;

                case 2:
                    _sprites!.ShakeTransform.Y = 0;
                    _wheelShake.Phase = 0;

                    if (_wheelShake.RemainingCycles > 0)
                    {
                        _wheelShake.RemainingCycles--;
                    }
                    break;
            }
        }
    }

    private void UpdateCatImage(IImage image)
    {
        CatImage.Source = image;
    }

    private void UpdateCounterText()
    {
        CounterText.Text = $"\\ {_behavior.Count} /";
    }

    private void UpdateContextMessage(string message)
    {
        CounterText.Text = message;
    }

    private void SetIdlePose()
    {
        UpdateCatImage(_sprites!.Idle);
    }

    private void ApplyFacing(bool isRightFacing)
    {
        _sprites!.FacingTransform.ScaleX = isRightFacing ? -1 : 1;
        _sprites!.FacingTransform.ScaleY = 1;
    }

    private void SetWalkFacingDirection()
    {
        ApplyFacing(_walk.DirectionX >= 0);
    }

    private void StartDrag(PointerPressedEventArgs e)
    {
        _behavior.IsDragging = true;
        _animation.GrabFrameIndex = 0;
        _animation.GrabFrameElapsed = 0.0;

        StopIdleBehaviors();
        ShowRandomDragMessage();

        UpdateCatImage(_sprites!.GrabImages[0]);
        BeginMoveDrag(e);
    }

    private void StopDrag()
    {
        if (!_behavior.IsDragging)
        {
            return;
        }

        _behavior.IsDragging = false;
        UpdateContextMessage(string.Empty);
        SetIdlePose();
        UpdateCounterText();
    }

    protected override void OnClosed(EventArgs e)
    {
        _animationTimer.Stop();
        _idleTimer.Stop();
        _tweetTimer.Stop();
        _hook.Dispose();

        base.OnClosed(e);
    }

    protected void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var currentPoint = e.GetCurrentPoint(CatImage);

        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!IsDragEnabledArea(currentPoint.Position.X, currentPoint.Position.Y))
        {
            return;
        }

        OnUserInput();
        StartDrag(e);
    }

    public void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        StopDrag();
    }

    protected void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_behavior.IsDragging)
        {
            return;
        }

        var currentPoint = e.GetCurrentPoint(CatImage);
        bool isDragArea = IsDragEnabledArea(currentPoint.Position.X, currentPoint.Position.Y);

        if (isDragArea && !_behavior.IsGrabCursor)
        {
            CatImage.Cursor = new Cursor(StandardCursorType.Hand);
            _behavior.IsGrabCursor = true;
            return;
        }

        if (!isDragArea && _behavior.IsGrabCursor)
        {
            CatImage.Cursor = new Cursor(StandardCursorType.Arrow);
            _behavior.IsGrabCursor = false;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private bool IsDragEnabledArea(double x, double y)
    {
        if (CatImage.Source is null)
        {
            return false;
        }

        double width = CatImage.Source.Size.Width;
        double height = CatImage.Source.Size.Height;

        return x >= DragThresholdX &&
               x < width &&
               y <= DragThresholdY &&
               y < height;
    }
    private void InitializeCharacters()
    {
        _characters.Clear();

        _characters.Add(new CharacterDefinition
        {
            Id = "cat",
            DisplayName = "ねこ",
            IdleFile = "cat/cat_up.png",
            ActionLeftFile = "cat/cat_left.png",
            ActionRightFile = "cat/cat_right.png",
            Grab1File = "cat/cat_grab_1.png",
            Grab2File = "cat/cat_grab_2.png",
            Walk1File = "cat/cat_walk_1.png",
            Walk2File = "cat/cat_walk_2.png"
        });

        _characters.Add(new CharacterDefinition
        {
            Id = "rabbit",
            DisplayName = "うさぎ",
            IdleFile = "rabbit/rabbit_up.png",
            ActionLeftFile = "rabbit/rabbit_left.png",
            ActionRightFile = "rabbit/rabbit_right.png",
            Grab1File = "rabbit/rabbit_grab_1.png",
            Grab2File = "rabbit/rabbit_grab_2.png",
            Walk1File = "rabbit/rabbit_walk_1.png",
            Walk2File = "rabbit/rabbit_walk_2.png"
        });
    }

    private void SwitchCharacter(string characterId)
    {
        var character = _characters.Find(x => x.Id == characterId);
        if (character is null)
        {
            return;
        }

        _currentCharacter = character;

        StopDrag();
        StopWalking();
        StopTweeting();

        _wheelShake.RemainingCycles = 0;
        _wheelShake.Phase = 0;
        _wheelShake.InitialCycles = 0;
        _wheelShake.Elapsed = 0;

        _sprites = LoadSprites(character);

        CatImage.RenderTransformOrigin = RelativePoint.Center;
        CatImage.RenderTransform = _sprites.TransformGroup;

        ApplyFacing(isRightFacing: false);
        UpdateCatImage(_sprites.Idle);
        UpdateCounterText();
        RefreshCharacterMenuChecks();
    }

    private void OnCharacterMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (CharacterMenuRoot is null)
        {
            return;
        }

        var items = new List<MenuItem>();

        foreach (var character in _characters)
        {
            var item = new MenuItem
            {
                Header = character.DisplayName,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = _currentCharacter?.Id == character.Id,
                Tag = character.Id
            };

            item.Click += OnCharacterMenuItemClick;
            items.Add(item);
        }

        CharacterMenuRoot.ItemsSource = items;
    }

    private void OnCharacterMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string characterId)
        {
            return;
        }

        SwitchCharacter(characterId);
    }

    private void RefreshCharacterMenuChecks()
    {
        if (CharacterMenuRoot?.Items is null)
        {
            return;
        }

        foreach (var obj in CharacterMenuRoot.Items)
        {
            if (obj is MenuItem item && item.Tag is string id)
            {
                item.IsChecked = _currentCharacter?.Id == id;
            }
        }
    }

    private sealed class CharacterDefinition
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required string IdleFile { get; init; }
        public required string ActionLeftFile { get; init; }
        public required string ActionRightFile { get; init; }
        public required string Grab1File { get; init; }
        public required string Grab2File { get; init; }
        public required string Walk1File { get; init; }
        public required string Walk2File { get; init; }
    }
}