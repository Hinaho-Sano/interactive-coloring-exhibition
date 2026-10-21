"""DICT_4X4_50 の ArUco マーカーを PNG 形式で生成するスクリプト。"""

from pathlib import Path

import cv2


# 印刷時にも輪郭が崩れにくいよう、マーカー本体を十分な解像度で生成します。
MARKER_SIZE_PX = 2000
# 検出に必要な白い余白（クワイエットゾーン）をマーカーの周囲に追加します。
MARGIN_SIZE_PX = 250
MARKER_IDS = (0, 1, 2, 3)


def generate_marker(dictionary, marker_id: int):
    """OpenCV のバージョン差を吸収してマーカー画像を生成します。"""
    if hasattr(cv2.aruco, "generateImageMarker"):
        return cv2.aruco.generateImageMarker(
            dictionary, marker_id, MARKER_SIZE_PX
        )

    # 古い OpenCV では generateImageMarker の代わりに drawMarker を使います。
    marker_image = cv2.aruco.drawMarker(
        dictionary, marker_id, MARKER_SIZE_PX
    )
    return marker_image


def main() -> None:
    output_dir = Path(__file__).resolve().parent.parent / "template"
    output_dir.mkdir(parents=True, exist_ok=True)

    dictionary = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)

    for marker_id in MARKER_IDS:
        marker_image = generate_marker(dictionary, marker_id)
        printable_image = cv2.copyMakeBorder(
            marker_image,
            MARGIN_SIZE_PX,
            MARGIN_SIZE_PX,
            MARGIN_SIZE_PX,
            MARGIN_SIZE_PX,
            cv2.BORDER_CONSTANT,
            value=255,
        )

        output_path = output_dir / f"aruco_{marker_id}.png"
        if not cv2.imwrite(str(output_path), printable_image):
            raise RuntimeError(f"画像を保存できませんでした: {output_path}")
        print(f"生成しました: {output_path}")


if __name__ == "__main__":
    # プロジェクト直下で次のコマンドを実行してください。
    # python ImagePipeline/scripts/generate_aruco_markers.py
    # cv2.aruco がない場合は、次のコマンドで OpenCV Contrib を導入してください。
    # python -m pip install opencv-contrib-python
    main()
