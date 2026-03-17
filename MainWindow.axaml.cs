using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SharpHook;
using System;
using System.Collections.Generic;

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
    private readonly Avalonia.Threading.DispatcherTimer _idleMsgChangeTimer;
    private DateTime _lastInputAt = DateTime.Now;
    private bool _isIdleMsgShowing = false;

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
        ApplyFacing(true);

        // アニメーション用のタイマー設定
        _animationTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _animationTimer.Tick += (s, e) =>
        {
            if (_isDragging)
            {
                CatImage.Source = _grabImages[_idxGrabImg];
                _idxGrabImg = (_idxGrabImg + 1) % _grabImages.Length;
            }
            else if (_isWalking)
            {
                SetWalkFacingDirection();
                CatImage.Source = _walkImages[_idxWalkImg];
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
        _idleMsgChangeTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _idleMsgChangeTimer.Tick += (s, e) =>
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

        // --- キーボードイベント（交互に動かす） ---
        _hook.KeyPressed += (s, e) =>
        {
            OnUserInput();
            UpdateCatPose(isKeyboard: true);
        };

        // --- マウスイベント（左右クリック） ---
        _hook.MousePressed += (s, e) =>
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
        };

        // ボタンを離した時は元に戻す
        _hook.KeyReleased += (s, e) =>
        {
            OnUserInput();
            ResetPose();
        };

        _hook.MouseReleased += (s, e) =>
        {
            OnUserInput();
            ResetPose();
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

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_isIdleMsgShowing)
            {
                _idleMsgChangeTimer.Stop();
                _isIdleMsgShowing = false;
                UpdateCounterText();
            }

            StopWalking();
        });
    }

    private void CheckIdle()
    {
        // 待機状態の判定処理
        if (_isDragging) return;
        if (_isIdleMsgShowing) return;
        if (_isWalking) return;

        var idleTime = DateTime.Now - _lastInputAt;

        // 一定時間以上待機が続いたら歩かせる
        if (idleTime >= TimeSpan.FromSeconds(1))
        {
            StartWalking();
        }

        // 一定時間以上待機が続いたらしゃべらせる
        if (idleTime >= TimeSpan.FromSeconds(15))
        {
            ShowRandomIdleMessage();
            _idleMsgChangeTimer.Start();
        }
    }

    private void UpdateCatPose(bool isKeyboard = false, bool? isLeftHand = null)
    {
        // ドラッグ中なら、キーボードやクリックによる画像更新をスキップ！
        if (_isDragging) return;

        // カウントを1増やす
        _count++;

        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_isIdleMsgShowing)
            {
                UpdateCounterText();
            }

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
            CatImage.Source = _actionImages[index];
        });
    }

    private void ResetPose()
    {
        // ドラッグ中なら、キーボードやクリックによる画像更新をスキップ！
        if (_isDragging) return;

        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            CatImage.Source = _idleImage;
        });
    }

    protected void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        OnUserInput();

        var currentPoint = e.GetCurrentPoint(CatImage); // 画像を基準にした座標を取得
        if (currentPoint.Properties.IsLeftButtonPressed)
        {
            if (IsDragEnableArea(currentPoint.Position.X, currentPoint.Position.Y))
            {
                _isDragging = true;

                // アニメーション開始！ 
                _idxGrabImg = 0;
                CatImage.Source = _grabImages[_idxGrabImg];
                ShowRandomDragMessage();
                _animationTimer.Start();

                BeginMoveDrag(e);
            }
        }
    }

    // 指を離した時は画像を戻すのを忘れずに！
    public void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        OnUserInput();

        if (_isDragging)
        {
            _isDragging = false;
            _animationTimer.Stop(); // アニメーション停止！
            CatImage.Source = _idleImage;
            UpdateCounterText();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // 全てのタイマーを停止する
        _walkTimer.Stop();
        _idleTimer.Stop();
        _idleMsgChangeTimer.Stop();
        _animationTimer.Stop();
        // フックを止める
        _hook.Dispose();
        base.OnClosed(e);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OnClosed(e);
    }

    protected void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        OnUserInput();

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

    private void UpdateCounterText()
    {
        CounterText.Text = $"\\ {_count} /";
    }

    private void ShowRandomDragMessage()
    {
        if (_dragMessages.Length == 0)
        {
            CounterText.Text = "";
            return;
        }

        var index = _random.Next(_dragMessages.Length);
        CounterText.Text = _dragMessages[index];
    }

    private void ShowRandomIdleMessage()
    {
        _isIdleMsgShowing = true;

        if (_idleMessages.Length == 0)
        {
            CounterText.Text = "……";
            return;
        }

        var index = _random.Next(_idleMessages.Length);
        CounterText.Text = _idleMessages[index];
    }

    private void StartWalking()
    {
        _isWalking = true;

        _walkDx = _random.Next(0, 2) == 0 ? -12 : 12;
        _walkDy = 0;

        SetWalkFacingDirection();

        _walkTimer.Start();
        _animationTimer.Start();
    }

    private void StopWalking()
    {
        if (!_isWalking) return;

        _walkTimer.Stop();
        _isWalking = false;
        _animationTimer.Stop();

        ApplyFacing(true);
    }

    private void WalkAround()
    {
        if (!_isWalking) return;
        if (_isDragging) return;

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

        // 左右端に当たったら反転
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

        // 上下ははみ出さないように軽く補正
        if (newY < minY) newY = minY;
        if (newY > maxY) newY = maxY;

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
}