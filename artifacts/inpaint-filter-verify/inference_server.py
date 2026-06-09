import os, sys
os.environ["KMP_DUPLICATE_LIB_OK"] = "TRUE"
os.environ["YOLO_VERBOSE"] = "False"
os.environ.setdefault("YOLO_CONFIG_DIR", os.path.dirname(os.path.abspath(__file__)))
try: sys.stdout.reconfigure(encoding="utf-8")
except: pass

import socket, struct, json, io, argparse, traceback, threading, warnings
warnings.filterwarnings("ignore")

print("[INFO] 패키지 로딩 중...", flush=True)
import torch
from ultralytics import YOLO
from PIL import Image
print(f"[INFO] PyTorch: {torch.__version__}", flush=True)

_model       = None
_class_names = {}   # {0: '금속 파손', 1: '비금속 파손'}
_task        = ""

# ── 신뢰도 임계값 ─────────────────────────────────────────────────
# 이 값 이상일 때만 NG 판정 (낮으면 OK 처리)
CONF_THR = 0.5

CMD_LOAD  = 0x01
CMD_INFER = 0x02
CMD_PING  = 0xFF


def load_model(path):
    global _model, _class_names, _task
    try:
        if not os.path.exists(path):
            return {"ok": False, "error": f"파일 없음: {path}"}

        size_mb = os.path.getsize(path) / 1024 / 1024
        print(f"[INFO] 모델 로드: {path} ({size_mb:.1f} MB)", flush=True)

        _model       = YOLO(path)

        # 모델에서 클래스 이름 읽기 후 금속/비금속 순서 보정
        # 학습 시 레이블 순서가 반대인 경우 아래에서 스왑
        raw_names = _model.names   # {0: ..., 1: ...}
        print(f"[INFO] 모델 원본 클래스: {raw_names}", flush=True)

        # 0번이 비금속 파손, 1번이 금속 파손으로 학습된 경우 → 스왑하여 표시 보정
        # (실제 모델 클래스 확인 후 필요없으면 아래 두 줄 제거)
        _class_names = dict(raw_names)

        _task        = _model.task

        print(f"[INFO] 태스크: {_task}", flush=True)
        print(f"[INFO] 클래스: {_class_names}", flush=True)
        print("[INFO] 모델 로드 완료!", flush=True)
        return {"ok": True, "task": _task, "classes": str(_class_names)}

    except Exception as e:
        msg = str(e)
        print(f"[ERROR] {msg}", flush=True)
        traceback.print_exc(file=sys.stdout)
        sys.stdout.flush()
        return {"ok": False, "error": msg}


def run_inference(img_bytes):
    if _model is None:
        return {"ok": False, "error": "모델 미로드"}
    try:
        img  = Image.open(io.BytesIO(img_bytes)).convert("RGB")
        w, h = img.size
        r    = _model.predict(img, verbose=False, conf=0.1)[0]
        polygons = []

        # ── 분류 모델 ───────────────────────────────────────────
        if _task == "classify" and r.probs is not None:
            cls  = int(r.probs.top1)
            conf = float(r.probs.top1conf.item())
            res  = _judge(cls, conf)
            res["polygons"] = []
            return res

        # ── 탐지 / 세그멘테이션 모델 ────────────────────────────
        best_cls, best_conf = -1, 0.0
        if r.boxes is not None and len(r.boxes) > 0:
            idx       = int(r.boxes.conf.argmax())
            best_cls  = int(r.boxes.cls[idx].item())
            best_conf = float(r.boxes.conf[idx].item())

            if r.masks is not None:
                for i, mask_xy in enumerate(r.masks.xy):
                    pts = [[round(float(x)/w, 4), round(float(y)/h, 4)]
                           for x, y in mask_xy.tolist()]
                    polygons.append({
                        "points":     pts,
                        "class_id":   int(r.boxes.cls[i].item()),
                        "class_name": _class_names.get(int(r.boxes.cls[i].item()), ""),
                        "confidence": round(float(r.boxes.conf[i].item()) * 100, 2),
                    })
            else:
                for i in range(len(r.boxes)):
                    b = r.boxes.xyxyn[i].tolist()
                    x1, y1, x2, y2 = b
                    c_i = int(r.boxes.cls[i].item())
                    polygons.append({
                        "points":     [[x1,y1],[x2,y1],[x2,y2],[x1,y2]],
                        "class_id":   c_i,
                        "class_name": _class_names.get(c_i, ""),
                        "confidence": round(float(r.boxes.conf[i].item()) * 100, 2),
                    })

        if best_cls >= 0:
            res = _judge(best_cls, best_conf)
        else:
            # 아무것도 검출 안 됨 → 정상 OK
            res = {"ok": True, "result": "OK", "defect": None,
                   "confidence": 0.0, "class_id": -1,
                   "class_name": "정상"}
        res["polygons"] = polygons
        return res

    except Exception as e:
        traceback.print_exc(file=sys.stdout)
        return {"ok": False, "error": str(e)}


def _judge(cls, conf):
    """
    2클래스 모델 판정 규칙
    ─────────────────────────────────────────
    클래스 0 : 금속 파손   → 항상 NG
    클래스 1 : 비금속 파손 → 항상 NG
    미검출(conf < CONF_THR) → OK (정상)
    ─────────────────────────────────────────
    클래스 이름 기반으로도 판정 (학습 레이블이 달라도 대응)
    """
    name = _class_names.get(cls, "").lower()

    # 신뢰도 미달 → OK
    if conf < CONF_THR:
        return {"ok": True, "result": "OK", "defect": None,
                "confidence": round(conf * 100, 2), "class_id": cls,
                "class_name": _class_names.get(cls, "")}

    # 정상 키워드가 있으면 OK
    ok_keys = ["ok", "pass", "normal", "good", "정상", "양품"]
    if any(k in name for k in ok_keys):
        return {"ok": True, "result": "OK", "defect": None,
                "confidence": round(conf * 100, 2), "class_id": cls,
                "class_name": _class_names.get(cls, "")}

    # 나머지(파손/결함 포함) → NG
    label = _class_names.get(cls, f"결함_{cls}")
    return {"ok": True, "result": "NG", "defect": label,
            "confidence": round(conf * 100, 2), "class_id": cls,
            "class_name": label}


# ── 소켓 프로토콜 ────────────────────────────────────────────────
def recv_exact(conn, n):
    buf = b""
    while len(buf) < n:
        c = conn.recv(n - len(buf))
        if not c: raise ConnectionResetError()
        buf += c
    return buf


def send_json(conn, data):
    p = json.dumps(data, ensure_ascii=False).encode()
    conn.sendall(struct.pack(">I", len(p)) + p)


def handle_client(conn, addr):
    print(f"[INFO] 연결: {addr}", flush=True)
    try:
        while True:
            hdr     = recv_exact(conn, 5)
            cmd     = hdr[0]
            length  = struct.unpack(">I", hdr[1:5])[0]
            payload = recv_exact(conn, length) if length > 0 else b""

            if   cmd == CMD_PING:  send_json(conn, {"ok": True})
            elif cmd == CMD_LOAD:  send_json(conn, load_model(payload.decode("utf-8")))
            elif cmd == CMD_INFER: send_json(conn, run_inference(payload))
            else:                  send_json(conn, {"ok": False, "error": f"CMD:{cmd}"})
    except: pass
    finally:
        conn.close()
        print(f"[INFO] 연결 종료: {addr}", flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=9999)
    ap.add_argument("--host", default="127.0.0.1")
    args = ap.parse_args()

    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((args.host, args.port))
    srv.listen(5)

    print(f"[INFO] 서버: {args.host}:{args.port}", flush=True)
    print("[READY]", flush=True)

    try:
        while True:
            conn, addr = srv.accept()
            threading.Thread(target=handle_client, args=(conn, addr), daemon=True).start()
    except KeyboardInterrupt:
        pass
    finally:
        srv.close()


if __name__ == "__main__":
    main()
