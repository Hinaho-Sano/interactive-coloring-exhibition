# Expanded exhibition prototype

大学祭展示向けに拡張している「めざめる物語の台地」の作業版です。

- `UnityProject/`: Unity 6プロジェクト本体
- `CoreTests/`: 動物と環境の純C#ロジックを確認する17項目のテスト
- `ImagePipeline/`: 四隅のArUcoマーカーを使った写真補正・猫の切り抜き・背景透過
- `ImagePipeline/template/cat_v2/sheet.pdf`: 現在の猫の塗り絵テンプレート

詳しい起動方法と操作は [`UnityProject/README.md`](UnityProject/README.md) を参照してください。

撮影画像は `ImagePipeline/input/` に置き、次のコマンドで処理します。

```powershell
python .\ImagePipeline\scripts\rectify_sheet.py --template cat_v2
```

生成された `ImagePipeline/output/*_cat_v2_transparent.png` はUnity側で自動的に読み込まれます。撮影画像と処理結果はGitへ追加しません。

環境インタラクションは動物版と独立しています。Unityの **Resonance → Open Environment Scene** で春の環境版、**Open Animal Scene** で塗り絵動物版を開きます。

