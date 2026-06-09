import os
os.environ["KMP_DUPLICATE_LIB_OK"] = "TRUE"

import sys, io, traceback
from PIL import Image

MODEL_PATH = r"C:\\project\\dong_mes_light\\2class_best.pt"

print(f"Python: {sys.version.split()[0]}")
print(f"파일 존재: {os.path.exists(MODEL_PATH)}")

import torch
from ultralytics import YOLO

print(f"\n--- 모델 로드 (YOLO) ---")
model = YOLO(MODEL_PATH)
print(f"태스크: {model.task}")
print(f"클래스: {model.names}")

print(f"\n--- 더미 이미지 추론 ---")
dummy = Image.new("RGB", (224, 224), color=(128, 128, 128))
results = model.predict(dummy, verbose=False, conf=0.1)
r = results[0]

if model.task == "classify" and r.probs is not None:
    conf = float(r.probs.top1conf.item())
    cls  = int(r.probs.top1)
    print(f"분류 결과: 클래스={cls} ({model.names[cls]}), 신뢰도={conf*100:.1f}%")
elif r.boxes is not None and len(r.boxes) > 0:
    print(f"탐지 결과: {len(r.boxes)}개 박스")
    for i, box in enumerate(r.boxes):
        cls  = int(box.cls.item())
        conf = float(box.conf.item())
        print(f"  [{i}] 클래스={cls} ({model.names[cls]}), 신뢰도={conf*100:.1f}%")
else:
    print("검출 없음")

print("\n✅ 테스트 완료!")
