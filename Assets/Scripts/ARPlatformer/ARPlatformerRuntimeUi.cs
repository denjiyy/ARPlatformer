using System;
using UnityEngine;
using UnityEngine.UI;

namespace ARPlatformer
{
    public readonly struct ARPlatformerRuntimeUiModel
    {
        public readonly string Headline;
        public readonly string Hint;
        public readonly string Stats;
        public readonly bool ShowFinishScanButton;
        public readonly bool FinishScanInteractable;
        public readonly bool ShowMarkerPreview;
        public readonly Texture MarkerPreviewTexture;
        public readonly bool ShowGameplayControls;
        public readonly bool RespawnInteractable;

        public ARPlatformerRuntimeUiModel(
            string headline,
            string hint,
            string stats,
            bool showFinishScanButton,
            bool finishScanInteractable,
            bool showMarkerPreview,
            Texture markerPreviewTexture,
            bool showGameplayControls,
            bool respawnInteractable)
        {
            Headline = headline;
            Hint = hint;
            Stats = stats;
            ShowFinishScanButton = showFinishScanButton;
            FinishScanInteractable = finishScanInteractable;
            ShowMarkerPreview = showMarkerPreview;
            MarkerPreviewTexture = markerPreviewTexture;
            ShowGameplayControls = showGameplayControls;
            RespawnInteractable = respawnInteractable;
        }
    }

    public sealed class ARPlatformerRuntimeUi : IDisposable
    {
        private Canvas _canvas;
        private Text _headlineText;
        private Text _hintText;
        private Text _statsText;
        private Button _finishScanButton;
        private Button _resetButton;
        private Button _jumpButton;
        private Button _respawnButton;
        private RawImage _markerPreview;
        private GameObject _markerPreviewRoot;
        private GameObject _gameplayControlsRoot;
        private TouchJoystick _joystick;
        private Sprite _whiteSprite;
        private Font _defaultFont;

        public TouchJoystick Joystick => _joystick;

        public void Build(
            Transform parent,
            Action onFinishScanPressed,
            Action onResetPressed,
            Action onRespawnPressed,
            Action onJumpPressed)
        {
            if (_canvas != null)
                return;

            _canvas = CreateCanvas(parent);
            var root = (RectTransform)_canvas.transform;

            var topPanel = CreatePanel(root, "Top Panel", new Color(0.05f, 0.08f, 0.1f, 0.72f));
            Stretch((RectTransform)topPanel.transform, new Vector2(0.04f, 0.78f), new Vector2(0.96f, 0.97f), Vector2.zero, Vector2.zero);

            _headlineText = CreateText(topPanel.transform, "Headline", 32, FontStyle.Bold, TextAnchor.UpperLeft);
            Stretch((RectTransform)_headlineText.transform, new Vector2(0.04f, 0.48f), new Vector2(0.6f, 0.9f), Vector2.zero, Vector2.zero);

            _hintText = CreateText(topPanel.transform, "Hint", 24, FontStyle.Normal, TextAnchor.UpperLeft);
            Stretch((RectTransform)_hintText.transform, new Vector2(0.04f, 0.08f), new Vector2(0.78f, 0.5f), Vector2.zero, Vector2.zero);
            _hintText.color = new Color(0.88f, 0.92f, 0.95f, 1f);

            _statsText = CreateText(topPanel.transform, "Stats", 24, FontStyle.Bold, TextAnchor.UpperRight);
            Stretch((RectTransform)_statsText.transform, new Vector2(0.56f, 0.5f), new Vector2(0.96f, 0.92f), Vector2.zero, Vector2.zero);
            _statsText.color = new Color(0.95f, 0.88f, 0.53f, 1f);

            _finishScanButton = CreateButton(root, "Finish Scan", new Vector2(280f, 96f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Color(0.12f, 0.54f, 0.44f, 0.92f));
            _finishScanButton.onClick.AddListener(() => onFinishScanPressed?.Invoke());

            _resetButton = CreateButton(root, "Reset Session", new Vector2(220f, 84f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-150f, -120f), new Color(0.66f, 0.25f, 0.16f, 0.92f), 24);
            _resetButton.onClick.AddListener(() => onResetPressed?.Invoke());

            _markerPreviewRoot = CreatePanel(root, "Marker Preview", new Color(0.05f, 0.08f, 0.1f, 0.78f));
            Stretch((RectTransform)_markerPreviewRoot.transform, new Vector2(0.3f, 0.15f), new Vector2(0.7f, 0.52f), Vector2.zero, Vector2.zero);

            _markerPreview = new GameObject("Marker Texture", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            _markerPreview.transform.SetParent(_markerPreviewRoot.transform, false);
            Stretch((RectTransform)_markerPreview.transform, new Vector2(0.18f, 0.24f), new Vector2(0.82f, 0.9f), Vector2.zero, Vector2.zero);
            _markerPreview.color = Color.white;

            var markerText = CreateText(_markerPreviewRoot.transform, "Marker Hint", 24, FontStyle.Normal, TextAnchor.UpperCenter);
            markerText.text = "Track this marker from a printout or a second screen to spawn the character.";
            Stretch((RectTransform)markerText.transform, new Vector2(0.08f, 0.04f), new Vector2(0.92f, 0.22f), Vector2.zero, Vector2.zero);
            markerText.alignment = TextAnchor.MiddleCenter;

            _gameplayControlsRoot = new GameObject("Gameplay Controls", typeof(RectTransform));
            _gameplayControlsRoot.transform.SetParent(root, false);
            Stretch((RectTransform)_gameplayControlsRoot.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            _joystick = CreateJoystick(_gameplayControlsRoot.transform);
            _respawnButton = CreateButton(
                _gameplayControlsRoot.transform,
                "Respawn",
                new Vector2(180f, 84f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 160f),
                new Color(0.15f, 0.39f, 0.72f, 0.92f),
                26);
            _respawnButton.onClick.AddListener(() => onRespawnPressed?.Invoke());

            _jumpButton = CreateButton(
                _gameplayControlsRoot.transform,
                "Jump",
                new Vector2(180f, 180f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-160f, 160f),
                new Color(0.93f, 0.54f, 0.17f, 0.92f),
                30);
            _jumpButton.onClick.AddListener(() => onJumpPressed?.Invoke());
        }

        public void Apply(ARPlatformerRuntimeUiModel model)
        {
            if (_headlineText == null || _hintText == null)
                return;

            _headlineText.text = model.Headline;
            _hintText.text = model.Hint;

            if (_statsText != null)
                _statsText.text = model.Stats;

            if (_finishScanButton != null)
            {
                _finishScanButton.gameObject.SetActive(model.ShowFinishScanButton);
                _finishScanButton.interactable = model.FinishScanInteractable;
            }

            if (_markerPreviewRoot != null)
                _markerPreviewRoot.SetActive(model.ShowMarkerPreview);

            if (_markerPreview != null)
                _markerPreview.texture = model.MarkerPreviewTexture;

            if (_gameplayControlsRoot != null)
                _gameplayControlsRoot.SetActive(model.ShowGameplayControls);

            if (_respawnButton != null)
                _respawnButton.interactable = model.RespawnInteractable;
        }

        public void Dispose()
        {
            if (_canvas != null)
                UnityEngine.Object.Destroy(_canvas.gameObject);

            if (_whiteSprite != null)
                UnityEngine.Object.Destroy(_whiteSprite);

            _whiteSprite = null;
            _defaultFont = null;
            _canvas = null;
            _headlineText = null;
            _hintText = null;
            _statsText = null;
            _finishScanButton = null;
            _resetButton = null;
            _jumpButton = null;
            _respawnButton = null;
            _markerPreview = null;
            _markerPreviewRoot = null;
            _gameplayControlsRoot = null;
            _joystick = null;
        }

        private Canvas CreateCanvas(Transform parent)
        {
            var canvasObject = new GameObject("AR Platformer Canvas");
            canvasObject.transform.SetParent(parent, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private GameObject CreatePanel(Transform parent, string name, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);

            var image = panel.GetComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.type = Image.Type.Sliced;
            image.color = color;

            return panel;
        }

        private Button CreateButton(
            Transform parent,
            string label,
            Vector2 size,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Color backgroundColor,
            int fontSize = 28)
        {
            var buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var rectTransform = (RectTransform)buttonObject.transform;
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.sizeDelta = size;
            rectTransform.anchoredPosition = anchoredPosition;

            var image = buttonObject.GetComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.color = backgroundColor;

            var text = CreateText(buttonObject.transform, "Label", fontSize, FontStyle.Bold, TextAnchor.MiddleCenter);
            text.text = label;
            Stretch((RectTransform)text.transform, Vector2.zero, Vector2.one, new Vector2(20f, 16f), new Vector2(-20f, -16f));

            return buttonObject.GetComponent<Button>();
        }

        private TouchJoystick CreateJoystick(Transform parent)
        {
            var joystickObject = new GameObject("Move Joystick", typeof(RectTransform), typeof(Image), typeof(TouchJoystick));
            joystickObject.transform.SetParent(parent, false);

            var rootRect = (RectTransform)joystickObject.transform;
            rootRect.anchorMin = new Vector2(0f, 0f);
            rootRect.anchorMax = new Vector2(0f, 0f);
            rootRect.sizeDelta = new Vector2(220f, 220f);
            rootRect.anchoredPosition = new Vector2(160f, 160f);

            var background = joystickObject.GetComponent<Image>();
            background.sprite = GetWhiteSprite();
            background.color = new Color(0.04f, 0.08f, 0.11f, 0.56f);

            var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(joystickObject.transform, false);

            var handleRect = (RectTransform)handleObject.transform;
            handleRect.sizeDelta = new Vector2(96f, 96f);

            var handleImage = handleObject.GetComponent<Image>();
            handleImage.sprite = GetWhiteSprite();
            handleImage.color = new Color(0.92f, 0.77f, 0.36f, 0.96f);

            var joystick = joystickObject.GetComponent<TouchJoystick>();
            joystick.Configure(handleRect, 72f);
            return joystick;
        }

        private Text CreateText(Transform parent, string name, int fontSize, FontStyle fontStyle, TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<Text>();
            text.font = GetDefaultFont();
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            text.supportRichText = false;

            return text;
        }

        private void Stretch(RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = offsetMin;
            rectTransform.offsetMax = offsetMax;
        }

        private Sprite GetWhiteSprite()
        {
            if (_whiteSprite != null)
                return _whiteSprite;

            _whiteSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f));

            return _whiteSprite;
        }

        private Font GetDefaultFont()
        {
            _defaultFont ??= Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _defaultFont;
        }
    }
}
