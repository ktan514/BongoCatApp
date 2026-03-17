using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SharpHook;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BongoCatAPP;

public partial class MainWindow : Window
{

    private readonly string ASSETS_PATH = "avares://BongoCatAPP/Assets/";

    private readonly string IMG_IDLE = "cat_up.png";
    private readonly string IMG_ACTION_1 = "cat_left.png";
    private readonly string IMG_ACTION_2 = "cat_right.png";
    private readonly string IMG_GRAB_1 = "cat_grab_1.png";
    private readonly string IMG_GRAB_2 = "cat_grab_2.png";
    private readonly string IMG_WALK_1 = "cat_walk_1.png";
    private readonly string IMG_WALK_2 = "cat_walk_2.png";

    private readonly Bitmap _idleImage;
    private readonly Bitmap[] _actionImages;
    private readonly Bitmap[] _grabImages;
    private readonly Bitmap[] _walkImages;


    private readonly TaskPoolGlobalHook _hook;
    private int _count = 0; // カウントを貯める変数

    // ドラッグ中アニメーション用制御変数
    private int _idxGrabImg = 0;
    private int _idxWalkImg = 0;
    private bool _isDragging = false; // ドラッグ中フラグ
    private readonly Avalonia.Threading.DispatcherTimer _animationTimer;

    private bool _isGrabCursor = false;

    // 待機セリフ用
    private readonly Avalonia.Threading.DispatcherTimer _idleTimer;
    private readonly Avalonia.Threading.DispatcherTimer _tweetTimer;
    private DateTime _lastInputAt = DateTime.Now;
    private bool _isTweeting = false;

    // ランダムセリフ表示用
    private readonly Random _random = new();
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

    // 待機中モーション
    private readonly Avalonia.Threading.DispatcherTimer _walkTimer;
    private bool _isWalking = false;
    private int _walkDx = 12;
    private int _walkDy = 0;

    public MainWindow()
    {
        InitializeComponent();

        // 画像の読み込み
        _idleImage = LoadImage(ASSETS_PATH + IMG_IDLE);
        _actionImages = [
                    LoadImage(ASSETS_PATH + IMG_ACTION_1),
                    LoadImage(ASSETS_PATH + IMG_ACTION_2)
        ];
        _grabImages = [
                    LoadImage(ASSETS_PATH + IMG_GRAB_1),
                    LoadImage(ASSETS_PATH + IMG_GRAB_2)
        ];
        _walkImages = [
                    LoadImage(ASSETS_PATH + IMG_WALK_1),
                    LoadImage(ASSETS_PATH + IMG_WALK_2)
        ];


        // 画像の向きを設定する
        ApplyFacing(false);

        // アニメーション用のタイマー設定
        _animationTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _animationTimer.Tick += (s, e) =>
        {
            if (_isDragging)
            {
                UpdateCatImage(_grabImages[_idxGrabImg]);
                _idxGrabImg = (_idxGrabImg + 1) % _grabImages.Length;
            }
            else if (_isWalking)
            {
                SetWalkFacingDirection();
                UpdateCatImage(_walkImages[_idxWalkImg]);
                _idxWalkImg = (_idxWalkImg + 1) % _walkImages.Length;
            }
        };

        // 15秒待機判定
        _idleTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _idleTimer.Tick += (s, e) => CheckIdle();
        _idleTimer.Start();

        // セリフを数秒後に更新する
        _tweetTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _tweetTimer.Tick += (s, e) =>
        {
            ShowRandomIdleMessage();
        };

        // 待機中モーション用タイマー設定
        _walkTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _walkTimer.Tick += (s, e) =>
        {
            WalkAround();
        };

        _hook = new TaskPoolGlobalHook();

        // キーを押した時
        _hook.KeyPressed += (s, e) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // ドラッグ中はカウントしない
                if (_isDragging)
                {
                    return;
                }

                OnUserInput();
                UpdateCatPose(isKeyboard: true);
            });
        };

        // ボタンを離した時
        _hook.KeyReleased += (s, e) =>
        {
            if (_isDragging) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ResetPose();
            });
        };

        // マウスのボタンを押した時
        _hook.MousePressed += (s, e) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnUserInput();

                if (e.Data.Button == SharpHook.Data.MouseButton.Button1)
                {
                    UpdateCatPose(isLeftHand: true);
                }
                else if (e.Data.Button == SharpHook.Data.MouseButton.Button2)
                {
                    UpdateCatPose(isLeftHand: false);
                }
            });
        };

        // マウスのボタンを離した時
        _hook.MouseReleased += (s, e) =>
        {
            if (_isDragging) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnUserInput();
                ResetPose();
            });
        };

        UpdateCounterText();
        _hook.RunAsync();
    }

    private static Bitmap LoadImage(string filePath)
    {
        return new Bitmap(AssetLoader.Open(new Uri(filePath)));
    }

    private void OnUserInput()
    {
        _lastInputAt = DateTime.Now;
        StopIdleBehaviors();
    }

    private void CheckIdle()
    {
        // 待機状態の判定処理
        if (_isDragging) return;
        if (_isTweeting) return;
        if (_isWalking) return;

        var idleTime = DateTime.Now - _lastInputAt;

        // 一定時間以上待機が続いたら歩かせる
        if (idleTime >= TimeSpan.FromMinutes(1))
        {
            StartWalking();
            return;
        }

        // 一定時間以上待機が続いたらしゃべらせる
        if (idleTime >= TimeSpan.FromSeconds(15))
        {
            StartTweeting();
            return;
        }
    }

    private void UpdateCatPose(bool isKeyboard = false, bool? isLeftHand = null)
    {
        // ドラッグ中なら、キーボードやクリックによる画像更新をスキップ！
        if (_isDragging) return;

        // カウントを1増やす
        _count++;
        UpdateCounterText();

        // ポーズの切り替え（前回のロジック）
        int index = 0;
        if (isKeyboard)
        {
            // キーボードならランダムに切り替え
            index = _random.Next(_actionImages.Length);
        }
        else if (isLeftHand.HasValue)
        {
            // マウスなら指定された方の手
            index = isLeftHand.Value ? 0 : 1;
        }
        UpdateCatImage(_actionImages[index]);
    }

    private async void ResetPose()
    {
        UpdateCatImage(_idleImage);
    }

    protected void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        OnUserInput();

        var currentPoint = e.GetCurrentPoint(CatImage); // 画像を基準にした座標を取得
        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }
        if (!IsDragEnableArea(currentPoint.Position.X, currentPoint.Position.Y))
        {
            return;
        }

        _isDragging = true;

        // アニメーション開始！ 
        _idxGrabImg = 0;
        UpdateCatImage(_grabImages[_idxGrabImg]);
        ShowRandomDragMessage();
        _animationTimer.Start();

        BeginMoveDrag(e);
    }

    // 指を離した時は画像を戻すのを忘れずに！
    public void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        OnUserInput();

        if (_isDragging)
        {
            StopDrag();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // 全てのタイマーを停止する
        _walkTimer.Stop();
        _idleTimer.Stop();
        _tweetTimer.Stop();
        _animationTimer.Stop();
        // フックを止める
        _hook.Dispose();
        base.OnClosed(e);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(e);
    }

    protected void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        // ドラッグ中なら無視
        if (_isDragging) return;

        var currentPoint = e.GetCurrentPoint(CatImage); // 画像を基準にした座標を取得

        if (IsDragEnableArea(currentPoint.Position.X, currentPoint.Position.Y))
        {
            // 範囲内に入ったら、カーソルを「グローブ（Hand）」にする
            if (!_isGrabCursor)
            {
                CatImage.Cursor = new Cursor(StandardCursorType.Hand);
                _isGrabCursor = true;
            }
        }
        else
        {
            // 範囲外に出たら、カーソルを「矢印（Arrow）」に戻す
            if (_isGrabCursor)
            {
                CatImage.Cursor = new Cursor(StandardCursorType.Arrow);
                _isGrabCursor = false;
            }
        }
    }

    private bool IsDragEnableArea(double x, double y)
    {
        double thresholdX = 100;
        double thresholdY = 50;

        // 画像のサイズを調べる
        if (CatImage.Source is null)
        {
            return false;
        }

        double width = CatImage.Source.Size.Width;
        double height = CatImage.Source.Size.Height;

        // 範囲判定
        return x >= thresholdX && x < width && y <= thresholdY && y < height;
    }

    private void UpdateCatImage(Bitmap source)
    {
        if (CatImage is null)
        {
            return;
        }
        CatImage.Source = source;
    }

    private void UpdateContextMssage(string text)
    {
        if (CounterText is null)
        {
            return;
        }
        CounterText.Text = text;
    }

    private void UpdateCounterText()
    {
        UpdateContextMssage($"\\ {_count} /");
    }

    private void ShowRandomDragMessage()
    {
        if (_dragMessages.Length == 0)
        {
            UpdateContextMssage("");
            return;
        }

        var index = _random.Next(_dragMessages.Length);
        UpdateContextMssage(_dragMessages[index]);
    }

    private void ShowRandomIdleMessage()
    {
        _isTweeting = true;

        if (_idleMessages.Length == 0)
        {
            UpdateContextMssage("……");
            return;
        }

        var index = _random.Next(_idleMessages.Length);
        UpdateContextMssage(_idleMessages[index]);
    }

    private void StartTweeting()
    {
        if (_isTweeting) return;

        ShowRandomIdleMessage();
        _tweetTimer.Start();
    }

    private void StopTweeting()
    {
        if (!_isTweeting) return;

        _tweetTimer.Stop();
        _isTweeting = false;
        _animationTimer.Stop();

        ApplyFacing(false);
    }

    private void StartWalking()
    {
        if (_isWalking) return;

        _isWalking = true;

        _walkDx = _random.Next(0, 2) == 0 ? -12 : 12;
        _walkDy = 0;

        UpdateContextMssage("");
        RandomizeWalkDirection();

        _walkTimer.Start();
        _animationTimer.Start();
    }

    private void StopWalking()
    {
        if (!_isWalking) return;

        _walkTimer.Stop();
        _isWalking = false;
        _animationTimer.Stop();

        ApplyFacing(false);
    }

    private void WalkAround()
    {
        if (!_isWalking) return;
        if (_isDragging) return;

        // ときどき進行方向を変える
        if (_random.Next(0, 20) == 0)
        {
            RandomizeWalkDirection();
        }

        var current = Position;

        int dx = _walkDx;
        int dy = _walkDy;

        // たまに少し上下に揺らす
        if (_random.Next(0, 8) == 0)
        {
            dy = _random.Next(-2, 3);
        }

        int newX = current.X + dx;
        int newY = current.Y + dy;

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

        // 左右端に当たったらX方向だけ反転
        if (newX <= minX)
        {
            newX = minX;
            _walkDx = Math.Abs(_walkDx);
            SetWalkFacingDirection();
        }
        else if (newX >= maxX)
        {
            newX = maxX;
            _walkDx = -Math.Abs(_walkDx);
            SetWalkFacingDirection();
        }

        // 上下端に当たったらY方向だけ反転
        if (newY <= minY)
        {
            newY = minY;
            _walkDy = Math.Abs(_walkDy);
        }
        else if (newY >= maxY)
        {
            newY = maxY;
            _walkDy = -Math.Abs(_walkDy);
        }

        Position = new PixelPoint(newX, newY);
    }

    private void ApplyFacing(bool faceRight)
    {
        CatImage.RenderTransformOrigin = RelativePoint.Center;
        CatImage.RenderTransform = faceRight
            ? new ScaleTransform(-1, 1)
            : new ScaleTransform(1, 1);
    }

    private void SetWalkFacingDirection()
    {
        // 右へ進むときは通常向き、左へ進むときは左右反転
        ApplyFacing(_walkDx >= 0);
    }

    private void StopIdleBehaviors()
    {
        StopTweeting();
        StopWalking();

        UpdateCounterText();
    }

    private void StopDrag()
    {
        _isDragging = false;
        _animationTimer.Stop();
        UpdateCatImage(_idleImage);
        UpdateCounterText();
    }

    private void RandomizeWalkDirection()
    {
        // 速さ候補
        int[] speeds = [6, 8, 10, 12];

        int dx;
        int dy;

        do
        {
            dx = speeds[_random.Next(speeds.Length)] * (_random.Next(0, 2) == 0 ? -1 : 1);
            dy = speeds[_random.Next(speeds.Length)] * (_random.Next(0, 2) == 0 ? -1 : 1);

            // 少しだけ上下移動を控えめにしたいなら dy を半分にする
            dy /= 2;
        }
        while (dx == 0 && dy == 0);

        _walkDx = dx;
        _walkDy = dy;

        SetWalkFacingDirection();
    }
}