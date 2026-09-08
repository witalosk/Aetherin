using UnityEngine;
using UnityEditor;
using System.IO;

namespace Aetherin.Editor
{

    public class SpriteSheetGridEditor : EditorWindow
    {
        private Texture2D targetTexture;
        private int columns = 4; // 横方向（アニメーションのフレーム数）
        private int rows = 4; // 縦方向（アニメーションの種類）

        // 単一マスの操作用
        private int swapIndexA = 0, swapIndexB = 1;
        private int flipTargetIndex = 0;

        // コマ位置調整（オフセット）用
        private int shiftTargetIndex = 0;
        private int shiftX = 0; // プラスで右へ
        private int shiftY = 0; // プラスで上へ

        // 行・列の操作用
        private int swapRowA = 0, swapRowB = 1;
        private int swapColA = 0, swapColB = 1;

        // 全体トリミング用
        private int cropTop = 0, cropBottom = 0, cropLeft = 0, cropRight = 0;

        // アニメーションプレビュー用
        private int selectedRow = 0;
        private float fps = 8f;
        private bool isPlaying = true;
        private int currentFrame = 0;
        private double lastStepTime;

        private Vector2 scrollPosition;

        [MenuItem("Tools/Sprite Sheet Grid Editor")]
        public static void ShowWindow()
        {
            GetWindow<SpriteSheetGridEditor>("Sprite Grid Editor").minSize = new Vector2(400, 600);
        }

        private void OnEnable()
        {
            EditorApplication.update += UpdateAnimation;
            lastStepTime = EditorApplication.timeSinceStartup;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateAnimation;
        }

        // アニメーション更新処理
        private void UpdateAnimation()
        {
            if (!isPlaying || targetTexture == null || columns <= 0) return;

            double currentTime = EditorApplication.timeSinceStartup;
            float frameDuration = 1f / Mathf.Max(1f, fps);

            if (currentTime - lastStepTime >= frameDuration)
            {
                currentFrame = (currentFrame + 1) % columns;
                lastStepTime = currentTime;
                Repaint();
            }
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("スプライトシート 編集ツール", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 1. 対象テクスチャの設定
            targetTexture = (Texture2D)EditorGUILayout.ObjectField("対象テクスチャ", targetTexture, typeof(Texture2D), false);
            columns = Mathf.Max(1, EditorGUILayout.IntField("フレーム数 (列 / 横)", columns));
            rows = Mathf.Max(1, EditorGUILayout.IntField("アニメーション数 (行 / 縦)", rows));

            if (targetTexture == null)
            {
                EditorGUILayout.HelpBox("編集したいSprite Sheet (Texture2D) をセットしてください。", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space();
            DrawLine();

            // --- アニメーション＆グリッド プレビュー ---
            DrawAnimationPreview();
            EditorGUILayout.Space();
            DrawGridPreview();

            EditorGUILayout.Space();
            DrawLine();

            // --- 位置調整機能 ---
            GUILayout.Label("1. コマ内の位置調整 (ズレ修正)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            shiftTargetIndex = EditorGUILayout.IntField("対象マス Index", shiftTargetIndex);
            shiftX = EditorGUILayout.IntField("X移動 (右+)", shiftX);
            shiftY = EditorGUILayout.IntField("Y移動 (上+)", shiftY);
            if (GUILayout.Button("位置を調整")) ShiftTile(shiftTargetIndex, shiftX, shiftY);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // --- グリッド操作機能 ---
            GUILayout.Label("2. マス・行・列の操作", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            flipTargetIndex = EditorGUILayout.IntField("左右反転マス Index", flipTargetIndex);
            if (GUILayout.Button("左右反転")) FlipTileHorizontal(flipTargetIndex);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            swapIndexA = EditorGUILayout.IntField("マス A", swapIndexA);
            swapIndexB = EditorGUILayout.IntField("マス B", swapIndexB);
            if (GUILayout.Button("A と B を入れ替え")) SwapTiles(swapIndexA, swapIndexB);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            swapRowA = EditorGUILayout.IntField("行 A (上から0...)", swapRowA);
            swapRowB = EditorGUILayout.IntField("行 B (上から0...)", swapRowB);
            if (GUILayout.Button("行 A と 行 B を入れ替え")) SwapRows(swapRowA, swapRowB);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            swapColA = EditorGUILayout.IntField("列 A (左から0...)", swapColA);
            swapColB = EditorGUILayout.IntField("列 B (左から0...)", swapColB);
            if (GUILayout.Button("列 A と 列 B を入れ替え")) SwapColumns(swapColA, swapColB);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // 保存ボタン (ピクセル操作の確定)
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("現在の変更を保存 (上書き)", GUILayout.Height(30))) SaveTexture(false);
            if (GUILayout.Button("別名で保存...", GUILayout.Height(30))) SaveTexture(true);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            DrawLine();

            // --- 全体トリミング機能 ---
            GUILayout.Label("3. 画像全体のトリミング", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("※トリミングは解像度が変わるため、実行すると即座に上書き保存されます。\n必要に応じて事前にバックアップを取ってください。",
                MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            cropTop = EditorGUILayout.IntField("上を削る (px)", cropTop);
            cropBottom = EditorGUILayout.IntField("下を削る (px)", cropBottom);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            cropLeft = EditorGUILayout.IntField("左を削る (px)", cropLeft);
            cropRight = EditorGUILayout.IntField("右を削る (px)", cropRight);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("トリミングを実行して保存", GUILayout.Height(25)))
            {
                CropTexture();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawLine() => EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        #region アニメーション＆グリッド プレビュー描画

        private void DrawAnimationPreview()
        {
            GUILayout.Label("🎬 アニメーション プレビュー", EditorStyles.boldLabel);

            selectedRow = Mathf.Clamp(EditorGUILayout.IntSlider("再生する行 (行 Index)", selectedRow, 0, rows - 1), 0,
                rows - 1);
            fps = EditorGUILayout.Slider("再生速度 (FPS)", fps, 1f, 30f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(isPlaying ? "一時停止 ⏸" : "再生 ▶", GUILayout.Height(25))) isPlaying = !isPlaying;
            EditorGUILayout.LabelField($"現在のコマ: {currentFrame} / {columns - 1}", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            float cellW = 1f / columns;
            float cellH = 1f / rows;
            float uMin = currentFrame * cellW;
            float vMin = (rows - 1 - selectedRow) * cellH;
            Rect uvRect = new Rect(uMin, vMin, cellW, cellH);

            Rect previewArea = GUILayoutUtility.GetRect(128, 128, GUILayout.ExpandWidth(false));
            Handles.DrawSolidRectangleWithOutline(previewArea, new Color(0.15f, 0.15f, 0.15f), Color.gray);

            if (targetTexture != null) GUI.DrawTextureWithTexCoords(previewArea, targetTexture, uvRect);
        }

        private void DrawGridPreview()
        {
            GUILayout.Label("🔍 シート全体のインデックス確認", EditorStyles.boldLabel);

            float previewSize = 250f;
            Rect previewRect = GUILayoutUtility.GetRect(previewSize, previewSize);

            if (Event.current.type == EventType.Repaint && targetTexture != null)
            {
                EditorGUI.DrawPreviewTexture(previewRect, targetTexture);

                float cellW = previewRect.width / columns;
                float cellH = previewRect.height / rows;

                GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        int index = r * columns + c;
                        Rect cellRect = new Rect(previewRect.x + c * cellW, previewRect.y + r * cellH, cellW, cellH);

                        bool isCurrent = (r == selectedRow && c == currentFrame && isPlaying);
                        labelStyle.normal.textColor = isCurrent ? Color.red : Color.yellow;
                        Handles.DrawSolidRectangleWithOutline(cellRect, Color.clear,
                            isCurrent ? Color.red : new Color(1, 1, 1, 0.3f));
                        GUI.Label(cellRect, index.ToString(), labelStyle);
                    }
                }
            }
        }

        #endregion

        #region ピクセル操作ロジック

        private bool EnsureTextureReadable(out Texture2D readableTex)
        {
            string path = AssetDatabase.GetAssetPath(targetTexture);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            // フォーマットを圧縮フォーマットではなく TextureFormat.RGBA32 に指定する
            readableTex = new Texture2D(targetTexture.width, targetTexture.height, TextureFormat.RGBA32, false);
    
            // GetPixels() で取得したピクセル情報をセット
            readableTex.SetPixels(targetTexture.GetPixels());
            readableTex.Apply();
            return true;
        }

        private Rect GetTileRect(int index, int texWidth, int texHeight)
        {
            int cellW = texWidth / columns;
            int cellH = texHeight / rows;
            int gridX = index % columns;
            int gridY = rows - 1 - (index / columns);
            return new Rect(gridX * cellW, gridY * cellH, cellW, cellH);
        }

        // ★ 新機能：コマ内の位置調整
        private void ShiftTile(int index, int shiftX, int shiftY)
        {
            if (shiftX == 0 && shiftY == 0) return;
            if (!EnsureTextureReadable(out Texture2D tex)) return;

            Rect rect = GetTileRect(index, tex.width, tex.height);
            int xMin = (int)rect.x, yMin = (int)rect.y;
            int w = (int)rect.width, h = (int)rect.height;

            Color[] original = tex.GetPixels(xMin, yMin, w, h);
            Color[] shifted = new Color[original.Length];

            // 背景を透明で初期化
            for (int i = 0; i < shifted.Length; i++) shifted[i] = Color.clear;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int newX = x + shiftX;
                    int newY = y + shiftY;

                    // マス内に収まる範囲だけ描画
                    if (newX >= 0 && newX < w && newY >= 0 && newY < h)
                    {
                        shifted[newY * w + newX] = original[y * w + x];
                    }
                }
            }

            tex.SetPixels(xMin, yMin, w, h, shifted);
            tex.Apply();
            ApplyToTarget(tex);
        }

        // 左右反転
        private void FlipTileHorizontal(int index)
        {
            if (!EnsureTextureReadable(out Texture2D tex)) return;

            Rect rect = GetTileRect(index, tex.width, tex.height);
            int xMin = (int)rect.x, yMin = (int)rect.y;
            int w = (int)rect.width, h = (int)rect.height;

            Color[] original = tex.GetPixels(xMin, yMin, w, h);
            Color[] flipped = new Color[original.Length];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) flipped[y * w + (w - 1 - x)] = original[y * w + x];
            }

            tex.SetPixels(xMin, yMin, w, h, flipped);
            tex.Apply();
            ApplyToTarget(tex);
        }

        // マスの入れ替え
        private void SwapTiles(int indexA, int indexB)
        {
            if (!EnsureTextureReadable(out Texture2D tex)) return;

            Rect rA = GetTileRect(indexA, tex.width, tex.height);
            Rect rB = GetTileRect(indexB, tex.width, tex.height);

            Color[] pixelsA = tex.GetPixels((int)rA.x, (int)rA.y, (int)rA.width, (int)rA.height);
            Color[] pixelsB = tex.GetPixels((int)rB.x, (int)rB.y, (int)rB.width, (int)rB.height);

            tex.SetPixels((int)rA.x, (int)rA.y, (int)rA.width, (int)rA.height, pixelsB);
            tex.SetPixels((int)rB.x, (int)rB.y, (int)rB.width, (int)rB.height, pixelsA);

            tex.Apply();
            ApplyToTarget(tex);
        }

        private void SwapRows(int rowA, int rowB)
        {
            if (rowA < 0 || rowA >= rows || rowB < 0 || rowB >= rows) return;
            for (int c = 0; c < columns; c++) SwapTiles(rowA * columns + c, rowB * columns + c);
        }

        private void SwapColumns(int colA, int colB)
        {
            if (colA < 0 || colA >= columns || colB < 0 || colB >= columns) return;
            for (int r = 0; r < rows; r++) SwapTiles(r * columns + colA, r * columns + colB);
        }

        private void ApplyToTarget(Texture2D editedTex)
        {
            Undo.RegisterCompleteObjectUndo(targetTexture, "Modify Sprite Sheet");
            targetTexture.SetPixels(editedTex.GetPixels());
            targetTexture.Apply();
        }

        private void SaveTexture(bool asNewFile)
        {
            string path = AssetDatabase.GetAssetPath(targetTexture);

            if (asNewFile)
            {
                path = EditorUtility.SaveFilePanelInProject("スプライトシートを保存", "NewSpriteSheet", "png", "保存先を選択してください");
                if (string.IsNullOrEmpty(path)) return;
            }

            byte[] bytes = targetTexture.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            AssetDatabase.Refresh();

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.isReadable = true;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.SaveAndReimport();
            }

            Debug.Log($"保存しました: {path}");
        }

        // ★ 新機能：全体トリミング処理
        private void CropTexture()
        {
            if (targetTexture == null) return;
            if (!EnsureTextureReadable(out Texture2D tex)) return;

            int newWidth = tex.width - cropLeft - cropRight;
            int newHeight = tex.height - cropTop - cropBottom;

            if (newWidth <= 0 || newHeight <= 0)
            {
                Debug.LogError("トリミング後のサイズが0以下になるためキャンセルしました。数値を調整してください。");
                return;
            }

            // Unityのテクスチャ座標は左下原点 (0,0)
            // bottomを削る = Yの開始位置が cropBottom
            // topを削る = 高さが newHeight になる
            Color[] newPixels = tex.GetPixels(cropLeft, cropBottom, newWidth, newHeight);

            Texture2D croppedTex = new Texture2D(newWidth, newHeight, tex.format, false);
            croppedTex.SetPixels(newPixels);
            croppedTex.Apply();

            string path = AssetDatabase.GetAssetPath(targetTexture);
            byte[] bytes = croppedTex.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            AssetDatabase.Refresh();

            Debug.Log($"トリミングして保存しました: {newWidth} x {newHeight} px");
        }

        #endregion

    }
}