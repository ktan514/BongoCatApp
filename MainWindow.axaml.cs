using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SharpHook;
using System;
using System.Diagnostics;

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

    public MainWindow()
    {
        InitializeComponent();

        // 画像の読み込み
        _baseImage = new Bitmap(AssetLoader.Open(new Uri("avares://BongoCatAPP/Assets/cat_up.png")));
        _leftImage = new Bitmap(AssetLoader.Open(new Uri("avares://BongoCatAPP/Assets/cat_left.png")));
        _rightImage = new Bitmap(AssetLoader.Open(new Uri("avares://BongoCatAPP/Assets/cat_right.png")));
        // _grabImage = new Bitmap(AssetLoader.Open(new Uri("avares://BongoCatAPP/Assets/cat_grab.png")));
        _grabImage1 = new Bitmap(AssetLoader.Open(new Uri("avares://BongoCatAPP/Assets/cat_grab_1.png")));
        _grabImage2 = new Bitmap(AssetLoader.Open(new Uri("avares://BongoCatAPP/Assets/cat_grab_2.png")));

        // アニメーション用のタイマー設定
        _animationTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80) // 0.15秒ごとにバタバタ
        };
        _animationTimer.Tick += (s, e) =>
        {
            // ドラッグ中だけ画像を交互に切り替える
            CatImage.Source = _isGrabImage1 ? _grabImage2 : _grabImage1;
            _isGrabImage1 = !_isGrabImage1;
        };

        _hook = new TaskPoolGlobalHook();

        // --- キーボードイベント（交互に動かす） ---
        _hook.KeyPressed += (s, e) => UpdateCatPose(isKeyboard: true);

        // --- マウスイベント（左右クリック） ---
        _hook.MousePressed += (s, e) =>
        {
            if (e.Data.Button == SharpHook.Data.MouseButton.Button1) // 左クリック
                UpdateCatPose(isLeftHand: true);
            else if (e.Data.Button == SharpHook.Data.MouseButton.Button2) // 右クリック
                UpdateCatPose(isLeftHand: false);
        };

        // ボタンを離した時は元に戻す
        _hook.KeyReleased += (s, e) => ResetPose();
        _hook.MouseReleased += (s, e) => ResetPose();

        UpdateCounterText();
        _hook.RunAsync();
    }

    private void UpdateCatPose(bool isKeyboard = false, bool? isLeftHand = null)
    {
        // ドラッグ中なら、キーボードやクリックによる画像更新をスキップ！
        if (_isDragging) return;

        // カウントを1増やす
        _count++;

        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            UpdateCounterText();

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

                this.BeginMoveDrag(e);
            }
        }
    }

    // 指を離した時は画像を戻すのを忘れずに！
    public void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
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
        this.Close();
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
        double _thresholdX = 100;
        double _thresholdY = 50;

        // 画像のサイズを調べる
        if (CatImage.Source is null)
        {
            return false;
        }
        double width = CatImage.Source.Size.Width;
        double height = CatImage.Source.Size.Height;

        // 範囲判定
        if (x >= _thresholdX && x < width && y <= _thresholdY && y < height)
        {
            return true;
        }

        return false;
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

}