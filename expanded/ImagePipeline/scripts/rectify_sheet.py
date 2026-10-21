"""テンプレート設定とArUcoを使って撮影画像を補正・透過します。"""

import argparse
import json
from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np


# 処理に必要なマーカー ID です。
MARKER_IDS = (0, 1, 2, 3)
SUPPORTED_EXTENSIONS = {".jpg", ".jpeg", ".png"}
DEFAULT_TEMPLATE_NAME = "cat_v1"

# 補正後の画像サイズ（横長テンプレート）です。
OUTPUT_WIDTH = 1600
OUTPUT_HEIGHT = 1000

# config.json がないテンプレートでも使える標準設定です。
DEFAULT_MARKER_LAYOUT = (1, 2, 3, 0)
DEFAULT_CROP_RECT = (220, 150, 1380, 850)

# 白～薄グレー背景を透明とみなす簡易判定のしきい値です。
# 将来は create_simple_hsv_foreground_mask() の代わりに固定マスクを渡せます。
BACKGROUND_MAX_SATURATION = 60
BACKGROUND_MIN_BRIGHTNESS = 120


@dataclass(frozen=True)
class TemplateConfig:
    """テンプレート固有のファイルと位置情報をまとめます。"""

    name: str
    directory: Path
    marker_layout: tuple[int, int, int, int]
    crop_rect: tuple[int, int, int, int]

    @property
    def sheet_path(self) -> Path:
        return self.directory / "sheet.pdf"

    @property
    def reference_path(self) -> Path:
        return self.directory / "reference.png"

    @property
    def mask_path(self) -> Path:
        return self.directory / "mask.png"


def read_image(path: Path) -> np.ndarray | None:
    """日本語などを含む Windows のパスでも画像を読み込めるようにします。"""
    try:
        encoded = np.fromfile(path, dtype=np.uint8)
    except OSError:
        return None
    return cv2.imdecode(encoded, cv2.IMREAD_COLOR)


def write_png(path: Path, image: np.ndarray) -> None:
    """PNG にエンコードし、パス文字種に依存せず保存します。"""
    success, encoded = cv2.imencode(".png", image)
    if not success:
        raise RuntimeError(f"PNG のエンコードに失敗しました: {path}")
    try:
        encoded.tofile(path)
    except OSError as exc:
        raise RuntimeError(f"画像の保存に失敗しました: {path}") from exc


def load_template_config(template_root: Path, template_name: str) -> TemplateConfig:
    """テンプレート用ディレクトリと任意のconfig.jsonを読み込みます。"""
    if not template_name or Path(template_name).name != template_name:
        raise ValueError(f"テンプレート名が不正です: {template_name}")

    template_dir = template_root / template_name
    if not template_dir.is_dir():
        raise FileNotFoundError(
            f"テンプレートディレクトリがありません: {template_dir}"
        )

    config_path = template_dir / "config.json"
    raw_config: dict[str, object] = {}
    if config_path.is_file():
        try:
            raw_config = json.loads(config_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise ValueError(f"config.jsonを読み込めません: {config_path}") from exc

    marker_layout = tuple(
        int(value)
        for value in raw_config.get("marker_layout", DEFAULT_MARKER_LAYOUT)
    )
    crop_rect = tuple(
        int(value) for value in raw_config.get("crop_rect", DEFAULT_CROP_RECT)
    )
    if len(marker_layout) != 4 or set(marker_layout) != set(MARKER_IDS):
        raise ValueError(
            "marker_layoutにはID 0,1,2,3を1回ずつ指定してください: "
            f"{marker_layout}"
        )
    if len(crop_rect) != 4:
        raise ValueError(f"crop_rectは4要素で指定してください: {crop_rect}")

    left, top, right, bottom = crop_rect
    if not (0 <= left < right <= OUTPUT_WIDTH and 0 <= top < bottom <= OUTPUT_HEIGHT):
        raise ValueError(f"crop_rectが補正画像の範囲外です: {crop_rect}")

    return TemplateConfig(
        name=template_name,
        directory=template_dir,
        marker_layout=marker_layout,
        crop_rect=crop_rect,
    )


def read_grayscale_image(path: Path) -> np.ndarray | None:
    """日本語パスに対応してグレースケール画像を読み込みます。"""
    try:
        encoded = np.fromfile(path, dtype=np.uint8)
    except OSError:
        return None
    return cv2.imdecode(encoded, cv2.IMREAD_GRAYSCALE)


def load_template_mask(template: TemplateConfig) -> np.ndarray | None:
    """mask.pngを読み込み、固定cropと同じサイズであることを確認します。"""
    if not template.mask_path.is_file():
        return None

    mask = read_grayscale_image(template.mask_path)
    if mask is None:
        raise ValueError(f"mask.pngを読み込めません: {template.mask_path}")

    left, top, right, bottom = template.crop_rect
    expected_shape = (bottom - top, right - left)
    if mask.shape != expected_shape:
        raise ValueError(
            "mask.pngのサイズが固定cropと一致しません: "
            f"mask={mask.shape[1]}x{mask.shape[0]}, "
            f"crop={expected_shape[1]}x{expected_shape[0]}"
        )
    return mask


def marker_area(corners: np.ndarray) -> float:
    """重複した ID が検出された場合の選択に使う面積を返します。"""
    return abs(float(cv2.contourArea(corners.astype(np.float32))))


def select_target_markers(
    corners: tuple[np.ndarray, ...] | list[np.ndarray],
    ids: np.ndarray | None,
) -> dict[int, np.ndarray]:
    """対象 ID ごとに、最も大きく検出されたマーカーの四隅を選びます。"""
    selected: dict[int, np.ndarray] = {}
    if ids is None:
        return selected

    for marker_corners, marker_id_value in zip(corners, ids.flatten()):
        marker_id = int(marker_id_value)
        if marker_id not in MARKER_IDS:
            continue

        points = marker_corners.reshape(4, 2)
        current = selected.get(marker_id)
        if current is None or marker_area(points) > marker_area(current):
            selected[marker_id] = points

    return selected


def draw_debug_image(
    image: np.ndarray,
    corners: tuple[np.ndarray, ...] | list[np.ndarray],
    ids: np.ndarray | None,
) -> np.ndarray:
    """検出枠、ID、四隅の番号、中心点を描画します。"""
    debug = image.copy()
    if ids is None or not corners:
        cv2.putText(
            debug,
            "No ArUco markers detected",
            (20, 40),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.9,
            (0, 0, 255),
            2,
            cv2.LINE_AA,
        )
        return debug

    cv2.aruco.drawDetectedMarkers(debug, corners, ids)

    for marker_corners, marker_id_value in zip(corners, ids.flatten()):
        points = marker_corners.reshape(4, 2)
        center = np.mean(points, axis=0)
        center_xy = tuple(np.rint(center).astype(int))

        # 各隅は ArUco の並び順（左上から時計回り）で 0～3 と表示します。
        for point_index, point in enumerate(points):
            point_xy = tuple(np.rint(point).astype(int))
            cv2.circle(debug, point_xy, 5, (0, 255, 255), -1, cv2.LINE_AA)
            cv2.putText(
                debug,
                str(point_index),
                (point_xy[0] + 6, point_xy[1] - 6),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                (0, 255, 255),
                2,
                cv2.LINE_AA,
            )

        cv2.drawMarker(
            debug,
            center_xy,
            (255, 0, 255),
            cv2.MARKER_CROSS,
            22,
            2,
            cv2.LINE_AA,
        )
        cv2.putText(
            debug,
            f"ID {int(marker_id_value)} center",
            (center_xy[0] + 10, center_xy[1] + 22),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.6,
            (255, 0, 255),
            2,
            cv2.LINE_AA,
        )

    return debug


def distance(point_a: np.ndarray, point_b: np.ndarray) -> float:
    """2点間のユークリッド距離を返します。"""
    return float(np.linalg.norm(point_a - point_b))


def order_corner_indices(points: np.ndarray) -> np.ndarray:
    """4点を左上・右上・右下・左下に並べるためのインデックスを返します。"""
    points = np.asarray(points, dtype=np.float32).reshape(4, 2)
    coordinate_sums = points[:, 0] + points[:, 1]
    coordinate_differences = points[:, 0] - points[:, 1]

    # 左上は x+y が最小、右下は最大、右上は x-y が最大、左下は最小です。
    indices = np.array(
        [
            int(np.argmin(coordinate_sums)),
            int(np.argmax(coordinate_differences)),
            int(np.argmax(coordinate_sums)),
            int(np.argmin(coordinate_differences)),
        ],
        dtype=np.int32,
    )
    if len(set(indices.tolist())) == 4:
        return indices

    # 強い遠近や同値で同じ点が複数回選ばれた場合は、重心周りの角度順を使います。
    center = np.mean(points, axis=0)
    angles = np.arctan2(points[:, 1] - center[1], points[:, 0] - center[0])
    angle_indices = np.argsort(angles)
    ordered = points[angle_indices]
    start = int(np.argmin(ordered[:, 0] + ordered[:, 1]))
    return np.roll(angle_indices, -start)


def order_corner_points(points: np.ndarray) -> np.ndarray:
    """4点を画像上の左上・右上・右下・左下の順に並べます。"""
    points = np.asarray(points, dtype=np.float32).reshape(4, 2)
    return points[order_corner_indices(points)]


def determine_clockwise_turns(
    corner_ids: tuple[int, ...],
    marker_layout: tuple[int, int, int, int],
) -> int:
    """現在のID配置をテンプレート配置へ合わせる時計回り回転数を返します。"""
    for turns in range(4):
        if turns == 0:
            rotated_ids = corner_ids
        else:
            rotated_ids = corner_ids[-turns:] + corner_ids[:-turns]
        if rotated_ids == marker_layout:
            return turns

    raise ValueError(
        "マーカーIDの並びがテンプレート配置の回転形ではありません: "
        f"{corner_ids}"
    )


def rotate_clockwise(image: np.ndarray, turns: int) -> np.ndarray:
    """画像を時計回りに90度単位で回転します。"""
    turns %= 4
    if turns == 1:
        return cv2.rotate(image, cv2.ROTATE_90_CLOCKWISE)
    if turns == 2:
        return cv2.rotate(image, cv2.ROTATE_180)
    if turns == 3:
        return cv2.rotate(image, cv2.ROTATE_90_COUNTERCLOCKWISE)
    return image


def rectify_image(
    image: np.ndarray,
    markers: dict[int, np.ndarray],
    marker_layout: tuple[int, int, int, int],
) -> tuple[np.ndarray, tuple[int, ...], int]:
    """射影補正とID配置に基づく向きの正規化を行います。"""
    centers = np.array(
        [
            np.mean(markers[marker_id], axis=0)
            for marker_id in MARKER_IDS
        ],
        dtype=np.float32,
    )

    # まず画像上の位置だけで四隅を並べ、その位置にあるIDから回転量を求めます。
    corner_indices = order_corner_indices(centers)
    source = centers[corner_indices]
    marker_id_array = np.asarray(MARKER_IDS)
    corner_ids = tuple(int(value) for value in marker_id_array[corner_indices])
    clockwise_turns = determine_clockwise_turns(corner_ids, marker_layout)

    # 90度または270度回す場合は、回転後が1600×1000になるよう先に縦横を入れ替えます。
    if clockwise_turns % 2 == 1:
        warp_width, warp_height = OUTPUT_HEIGHT, OUTPUT_WIDTH
    else:
        warp_width, warp_height = OUTPUT_WIDTH, OUTPUT_HEIGHT

    destination = np.array(
        [
            [0, 0],
            [warp_width - 1, 0],
            [warp_width - 1, warp_height - 1],
            [0, warp_height - 1],
        ],
        dtype=np.float32,
    )
    transform = cv2.getPerspectiveTransform(source, destination)
    warped = cv2.warpPerspective(image, transform, (warp_width, warp_height))
    normalized = rotate_clockwise(warped, clockwise_turns)
    return normalized, corner_ids, clockwise_turns


def draw_fixed_crop_debug(
    rectified: np.ndarray,
    corner_ids: tuple[int, ...],
    clockwise_turns: int,
    template: TemplateConfig,
    mask_source: str,
) -> np.ndarray:
    """向き判定と、固定cropおよび最終出力の範囲を描画します。"""
    debug = rectified.copy()
    id_text = ",".join(str(marker_id) for marker_id in corner_ids)
    cv2.putText(
        debug,
        f"orientation: corner IDs={id_text}; rotate={clockwise_turns * 90} deg CW",
        (30, 45),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.8,
        (255, 80, 0),
        2,
        cv2.LINE_AA,
    )
    layout = template.marker_layout
    normalized_layout_text = (
        f"normalized layout: TL={layout[0]} TR={layout[1]} "
        f"BR={layout[2]} BL={layout[3]}"
    )
    cv2.putText(
        debug,
        normalized_layout_text,
        (30, 80),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.75,
        (255, 80, 0),
        2,
        cv2.LINE_AA,
    )

    cv2.putText(
        debug,
        f"template={template.name}; mask={mask_source}",
        (30, 115),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.75,
        (255, 80, 0),
        2,
        cv2.LINE_AA,
    )

    left, top, right, bottom = template.crop_rect
    # 固定cropと最終出力キャンバスは同一範囲です。外側を緑、内側を黄で示します。
    cv2.rectangle(
        debug,
        (left, top),
        (right, bottom),
        (0, 255, 0),
        5,
    )
    cv2.rectangle(
        debug,
        (left + 3, top + 3),
        (right - 3, bottom - 3),
        (0, 255, 255),
        2,
    )
    crop_width = right - left
    crop_height = bottom - top
    cv2.putText(
        debug,
        f"fixed crop = final canvas: {crop_width}x{crop_height}",
        (left, max(150, top - 15)),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.8,
        (0, 180, 0),
        2,
        cv2.LINE_AA,
    )
    return debug


def crop_fixed_cat_area(
    rectified: np.ndarray,
    crop_rect: tuple[int, int, int, int],
) -> np.ndarray:
    """正規化済み画像からテンプレート基準の固定猫エリアを切り出します。"""
    left, top, right, bottom = crop_rect
    if not (
        0 <= left < right <= rectified.shape[1]
        and 0 <= top < bottom <= rectified.shape[0]
    ):
        raise ValueError(
            "固定猫エリアが補正画像の範囲外です: "
            f"({left}, {top})-({right}, {bottom})"
        )

    return rectified[top:bottom, left:right].copy()


def create_simple_hsv_foreground_mask(image: np.ndarray) -> np.ndarray:
    """HSV値から猫を残す簡易前景マスク（0～255）を生成します。"""
    hsv = cv2.cvtColor(image, cv2.COLOR_BGR2HSV)
    saturation = hsv[:, :, 1]
    brightness = hsv[:, :, 2]
    background_mask = (
        (saturation <= BACKGROUND_MAX_SATURATION)
        & (brightness >= BACKGROUND_MIN_BRIGHTNESS)
    ).astype(np.uint8) * 255

    # 背景上の微小な色ノイズを埋め、猫の輪郭付近は軽くなじませます。
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3))
    background_mask = cv2.morphologyEx(
        background_mask, cv2.MORPH_CLOSE, kernel
    )
    return cv2.GaussianBlur(255 - background_mask, (3, 3), 0)


def create_foreground_mask(
    image: np.ndarray,
    template_mask: np.ndarray | None,
) -> tuple[np.ndarray, str]:
    """テンプレートマスクを優先し、なければ簡易HSVマスクを返します。"""
    if template_mask is not None:
        # mask.pngは白ほど不透明、黒ほど透明としてそのままアルファに使います。
        return template_mask.copy(), "mask.png"
    return create_simple_hsv_foreground_mask(image), "HSV fallback"


def apply_foreground_mask(image: np.ndarray, foreground_mask: np.ndarray) -> np.ndarray:
    """前景マスクをアルファチャンネルとしてBGR画像へ適用します。"""
    if foreground_mask.shape != image.shape[:2]:
        raise ValueError(
            "前景マスクのサイズがcrop画像と一致しません: "
            f"image={image.shape[:2]}, mask={foreground_mask.shape}"
        )
    if foreground_mask.dtype != np.uint8:
        foreground_mask = np.clip(foreground_mask, 0, 255).astype(np.uint8)

    transparent = cv2.cvtColor(image, cv2.COLOR_BGR2BGRA)
    transparent[:, :, 3] = foreground_mask
    return transparent


def make_background_transparent(
    image: np.ndarray,
    foreground_mask: np.ndarray,
) -> np.ndarray:
    """選択済み前景マスクを使い、キャンバスを変えずに背景を透明化します。"""
    return apply_foreground_mask(image, foreground_mask)


def create_detector() -> object:
    """新旧 OpenCV の API 差を吸収して ArUco 検出器を用意します。"""
    dictionary = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)
    parameters = cv2.aruco.DetectorParameters()
    if hasattr(cv2.aruco, "ArucoDetector"):
        return cv2.aruco.ArucoDetector(dictionary, parameters)
    return dictionary, parameters


def detect_markers(detector: object, image: np.ndarray):
    """利用中の OpenCV に合う API でマーカーを検出します。"""
    if hasattr(detector, "detectMarkers"):
        return detector.detectMarkers(image)

    dictionary, parameters = detector
    return cv2.aruco.detectMarkers(
        image, dictionary, parameters=parameters
    )


def parse_arguments() -> argparse.Namespace:
    """コマンドラインから使用するテンプレート名を受け取ります。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--template",
        default=DEFAULT_TEMPLATE_NAME,
        help=f"template配下のテンプレート名（既定: {DEFAULT_TEMPLATE_NAME}）",
    )
    return parser.parse_args()


def main(template_name: str = DEFAULT_TEMPLATE_NAME) -> None:
    pipeline_dir = Path(__file__).resolve().parent.parent
    input_dir = pipeline_dir / "input"
    output_dir = pipeline_dir / "output"
    debug_dir = output_dir / "debug"
    template_root = pipeline_dir / "template"
    output_dir.mkdir(parents=True, exist_ok=True)
    debug_dir.mkdir(parents=True, exist_ok=True)

    template = load_template_config(template_root, template_name)
    template_mask = load_template_mask(template)
    print(f"使用テンプレート: {template.name} ({template.directory})")
    if template_mask is None:
        print("  mask.pngなし: 簡易HSV透過を使用します")
    else:
        print(f"  mask.pngを使用します: {template.mask_path}")

    image_paths = sorted(
        (
            path
            for path in input_dir.iterdir()
            if path.is_file() and path.suffix.lower() in SUPPORTED_EXTENSIONS
        ),
        key=lambda path: (path.name.lower(), path.name),
    )

    if not image_paths:
        print(f"対象画像がありません: {input_dir}")
        return

    detector = create_detector()

    for image_path in image_paths:
        print(f"処理中: {image_path.name}")
        image = read_image(image_path)
        if image is None:
            print(f"  読み込み失敗のためスキップしました: {image_path}")
            continue

        corners, ids, _rejected = detect_markers(detector, image)
        selected = select_target_markers(corners, ids)

        # 4つ揃わない場合でも、確認できるようデバッグ画像は保存します。
        debug_image = draw_debug_image(image, corners, ids)
        debug_path = debug_dir / f"{image_path.stem}_debug.png"
        write_png(debug_path, debug_image)
        print(f"  デバッグ画像: {debug_path}")

        missing_ids = [marker_id for marker_id in MARKER_IDS if marker_id not in selected]
        if missing_ids:
            missing_text = ", ".join(str(marker_id) for marker_id in missing_ids)
            # 重要な警告は、端末の文字コードに左右されない ASCII でも表示します。
            print(f"  Missing marker IDs: {missing_text} (rectification skipped)")
            continue

        try:
            rectified, corner_ids, clockwise_turns = rectify_image(
                image, selected, template.marker_layout
            )
        except ValueError as exc:
            print(f"  Orientation normalization failed: {exc}")
            continue

        rotation_degrees = clockwise_turns * 90
        print(
            "  Orientation: "
            f"corner IDs={corner_ids}, rotate={rotation_degrees} deg CW"
        )
        output_path = output_dir / f"{image_path.stem}_rectified.png"
        write_png(output_path, rectified)
        print(f"  補正画像: {output_path}")

        cat_image = crop_fixed_cat_area(rectified, template.crop_rect)
        cat_path = output_dir / f"{image_path.stem}_cat.png"
        write_png(cat_path, cat_image)
        print(f"  固定猫エリア: {cat_path}")

        foreground_mask, mask_source = create_foreground_mask(
            cat_image, template_mask
        )
        mask_debug_path = debug_dir / f"{image_path.stem}_mask_debug.png"
        write_png(mask_debug_path, foreground_mask)
        print(f"  適用マスク ({mask_source}): {mask_debug_path}")

        crop_debug = draw_fixed_crop_debug(
            rectified,
            corner_ids,
            clockwise_turns,
            template,
            mask_source,
        )
        crop_debug_path = debug_dir / f"{image_path.stem}_crop_debug.png"
        write_png(crop_debug_path, crop_debug)
        print(f"  固定cropデバッグ画像: {crop_debug_path}")

        # 透明化ではアルファ値だけを追加し、再cropやリサイズは行いません。
        transparent = make_background_transparent(cat_image, foreground_mask)
        if transparent.shape[:2] != cat_image.shape[:2]:
            raise RuntimeError("透過処理によってキャンバスサイズが変化しました")
        # Keep legacy cat_v1 filenames unchanged. New rigged templates include
        # their ID so Unity can choose the matching part masks and orientation.
        template_suffix = "" if template.name == DEFAULT_TEMPLATE_NAME else f"_{template.name}"
        transparent_path = output_dir / f"{image_path.stem}{template_suffix}_transparent.png"
        write_png(transparent_path, transparent)
        print(f"  背景透過画像: {transparent_path}")


if __name__ == "__main__":
    arguments = parse_arguments()
    main(arguments.template)
