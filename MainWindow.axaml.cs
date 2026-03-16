using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SharpHook;
using System;

namespace BongoCatAPP;

public partial class MainWindow : Window
{
    private readonly TaskPoolGlobalHook _hook;
    private readonly Bitmap _baseImage, _leftImage, _rightImage;
    private int _count = 0; // カウントを貯める変数

    // ドラッグ中アニメーション用制御変数
    private readonly Bitmap _grabImage1, _grabImage2; // アニメーション用
    private bool _isGrabImage1 = true;
    private bool _isNextLeft = true; // 次にどっちの手を動かすかのフラグ
    private bool _isDragging = false; // ドラッグ中フラグ
    private readonly Avalonia.Threading.DispatcherTimer _animationTimer;

    private bool _isGrabCursor = false;

    // 待機セリフ用
    private readonly Avalonia.Threading.DispatcherTimer _idleTimer;
    private readonly Avalonia.Threading.DispatcherTimer _speechHideTimer;
    private DateTime _lastInputAt = DateTime.Now;
    private bool _isIdleSpeechShowing = false;
    private bool _hasShownIdleSpeechSinceLastInput = false;

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
        "チャッピーはここだよ",
        "ちょこん"
    ];

    public MainWindow()
    {
        InitializeComponent();

        // 画像の読み込み
        _baseImage = new Bitmap(AssetLoader.Open(new Uri("avares://BongoCatAPP/Assets/cat_up.png")));
        _leftImage = new Bitmap(AssetLoader.Open(new Uri("avares://BongoCatAPP/Assets/cat_left.png")));
        _rightImage = new Bitmap(AssetLoader.Open(new Uri("avares://BongoCatAPP/Assets/cat_right.png")));
        _grabImage1 = new Bitmap(AssetLoader.Open(new Uri("avares://BongoCatAPP/Assets/cat_grab_1.png")));
        _grabImage2 = new Bitmap(AssetLoader.Open(new Uri("avares://BongoCatAPP/Assets/cat_grab_2.png")));

        // アニメーション用のタイマー設定
        _animationTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _animationTimer.Tick += (s, e) =>
        {
            // ドラッグ中だけ画像を交互に切り替える
            CatImage.Source = _isGrabImage1 ? _grabImage2 : _grabImage1;
            _isGrabImage1 = !_isGrabImage1;
        };

        // 15秒待機判定
        _idleTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _idleTimer.Tick += (s, e) => CheckIdle();
        _idleTimer.Start();

        // セリフを5秒後に消す
        _speechHideTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _speechHideTimer.Tick += (s, e) =>
        {
            _speechHideTimer.Stop();
            HideIdleSpeech();
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

    private void OnUserInput()
    {
        _lastInputAt = DateTime.Now;
        _hasShownIdleSpeechSinceLastInput = false;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _speechHideTimer.Stop();

            if (_isIdleSpeechShowing)
            {
                HideIdleSpeech();
            }
        });
    }

    private void CheckIdle()
    {
        if (_isDragging) return;
        if (_isIdleSpeechShowing) return;
        if (_hasShownIdleSpeechSinceLastInput) return;

        var idleTime = DateTime.Now - _lastInputAt;
        if (idleTime < TimeSpan.FromSeconds(15)) return;

        _hasShownIdleSpeechSinceLastInput = true;
        ShowRandomIdleMessage();
    }

    private void HideIdleSpeech()
    {
        _isIdleSpeechShowing = false;
        UpdateCounterText();
    }

    private void UpdateCatPose(bool isKeyboard = false, bool? isLeftHand = null)
    {
        // ドラッグ中なら、キーボードやクリックによる画像更新をスキップ！
        if (_isDragging) return;

        // カウントを1増やす
        _count++;

        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_isIdleSpeechShowing)
            {
                UpdateCounterText();
            }

            // ポーズの切り替え（前回のロジック）
            if (isKeyboard)
            {
                // キーボードなら交互に切り替え
                CatImage.Source = _isNextLeft ? _leftImage : _rightImage;
                _isNextLeft = !_isNextLeft;
            }
            else if (isLeftHand.HasValue)
            {
                // マウスなら指定された方の手
                CatImage.Source = isLeftHand.Value ? _leftImage : _rightImage;
            }
        });
    }

    private void ResetPose()
    {
        // ドラッグ中なら、キーボードやクリックによる画像更新をスキップ！
        if (_isDragging) return;

        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            CatImage.Source = _baseImage;
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _hook.Dispose();
        base.OnClosed(e);
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
                _isGrabImage1 = true;
                CatImage.Source = _grabImage1;
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
            CatImage.Source = _baseImage;
            UpdateCounterText();
        }
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
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
        if (_idleMessages.Length == 0)
        {
            CounterText.Text = "……";
            return;
        }

        var index = _random.Next(_idleMessages.Length);
        CounterText.Text = _idleMessages[index];

        _speechHideTimer.Stop();
        _speechHideTimer.Start();
    }
}