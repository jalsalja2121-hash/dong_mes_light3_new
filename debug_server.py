"""
디버그용 추론 서버 - 모든 오류 상세 출력
"""
import os, sys
os.environ["KMP_DUPLICATE_LIB_OK"] = "TRUE"
os.environ["YOLO_VERBOSE"] = "False"
try: sys.stdout.reconfigure(encoding="utf-8")
except: pass

import socket, struct, json, io, traceback, threading, warnings
warnings.filterwarnings("ignore")

print("[DEBUG] 패키지 로딩 시작...", flush=True)
import torch
print(f"[DEBUG] torch OK: {torch.__version__}", flush=True)

from ultralytics import YOLO
print(f"[DEBUG] ultralytics OK", flush=True)

from PIL import Image
print(f"[DEBUG] PIL OK", flush=True)

_model = None
CMD_LOAD=0x01; CMD_INFER=0x02; CMD_PING=0xFF

def recv_exact(conn, n):
    buf=b""
    while len(buf)<n:
        c=conn.recv(n-len(buf))
        if not c: raise ConnectionResetError()
        buf+=c
    return buf

def send_json(conn, data):
    p=json.dumps(data,ensure_ascii=False).encode()
    conn.sendall(struct.pack(">I",len(p))+p)

def handle_client(conn, addr):
    global _model
    print(f"[DEBUG] 클라이언트 연결: {addr}", flush=True)
    try:
        while True:
            hdr    = recv_exact(conn, 5)
            cmd    = hdr[0]
            length = struct.unpack(">I", hdr[1:5])[0]
            payload= recv_exact(conn, length) if length>0 else b""
            print(f"[DEBUG] CMD={cmd:#04x} LEN={length}", flush=True)

            if cmd == CMD_PING:
                send_json(conn, {"ok": True})

            elif cmd == CMD_LOAD:
                path = payload.decode("utf-8")
                print(f"[DEBUG] 모델 로드 시작: {path}", flush=True)
                try:
                    _model = YOLO(path)
                    print(f"[DEBUG] YOLO() 완료: task={_model.task} names={_model.names}", flush=True)
                    send_json(conn, {"ok": True, "task": _model.task, "classes": str(_model.names)})
                    print(f"[DEBUG] 응답 전송 완료", flush=True)
                except Exception as e:
                    print(f"[DEBUG] 로드 예외: {e}", flush=True)
                    traceback.print_exc(file=sys.stdout)
                    sys.stdout.flush()
                    send_json(conn, {"ok": False, "error": str(e)})

            elif cmd == CMD_INFER:
                if _model is None:
                    send_json(conn, {"ok": False, "error": "모델 미로드"})
                else:
                    img = Image.open(io.BytesIO(payload)).convert("RGB")
                    r   = _model.predict(img, verbose=False, conf=0.1)[0]
                    if _model.task=="classify" and r.probs is not None:
                        cls  = int(r.probs.top1)
                        conf = float(r.probs.top1conf.item())
                        name = _model.names.get(cls,"").lower()
                        res  = "OK" if conf<0.5 or any(k in name for k in ["ok","정상","normal","good"]) else "NG"
                        send_json(conn, {"ok":True,"result":res,"defect":None if res=="OK" else _model.names.get(cls),"confidence":round(conf*100,2),"class_id":cls})
                    else:
                        send_json(conn, {"ok":True,"result":"OK","defect":None,"confidence":0.0,"class_id":-1})
    except Exception as e:
        print(f"[DEBUG] 핸들러 예외: {e}", flush=True)
        traceback.print_exc()
    finally:
        conn.close()
        print(f"[DEBUG] 연결 종료: {addr}", flush=True)

srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
srv.bind(("127.0.0.1", 9999))
srv.listen(5)
print("[READY]", flush=True)

while True:
    conn, addr = srv.accept()
    threading.Thread(target=handle_client, args=(conn,addr), daemon=True).start()
