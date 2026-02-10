import crypto from "node:crypto";
import { MqEventCodec, type MqEvent } from "./proto/mics_hook.js";

function toLeI64Bytes(value: number): Uint8Array {
  const buf = Buffer.alloc(8);
  buf.writeBigInt64LE(BigInt(value), 0);
  return buf;
}

export function computeHookSignBase64(tenantSecret: string, meta: { requestId: string; timestampMs: number }, payloadBytes: Uint8Array): string {
  const key = Buffer.from(tenantSecret ?? "", "utf8");
  const requestIdBytes = Buffer.from(meta.requestId ?? "", "utf8");
  const tsBytes = toLeI64Bytes(meta.timestampMs ?? 0);

  return crypto.createHmac("sha256", key).update(payloadBytes).update(requestIdBytes).update(tsBytes).digest("base64");
}

export function verifyHookSign(
  tenantSecret: string,
  meta: { requestId: string; timestampMs: number },
  payloadBytes: Uint8Array,
  signBase64: string
): boolean {
  if (!signBase64) return false;
  const expected = computeHookSignBase64(tenantSecret, meta, payloadBytes);
  return constantTimeEqualBase64(expected, signBase64);
}

function computeMqBytesSignBase64(tenantSecret: string, payloadBytes: Uint8Array): string {
  const key = Buffer.from(tenantSecret ?? "", "utf8");
  return crypto.createHmac("sha256", key).update(payloadBytes).digest("base64");
}

export function computeMqEventSignBase64(tenantSecret: string, event: MqEvent): string {
  const bytes = MqEventCodec.encode({ ...event, sign: "" }).finish();
  return computeMqBytesSignBase64(tenantSecret, bytes);
}

export function verifyMqEventSign(tenantSecret: string, event: MqEvent, signBase64: string): boolean {
  if (!signBase64) return false;
  const expected = computeMqEventSignBase64(tenantSecret, event);
  return constantTimeEqualBase64(expected, signBase64);
}

function constantTimeEqualBase64(a: string, b: string): boolean {
  try {
    if (!isCanonicalBase64(b)) return false;
    const ab = Buffer.from(a, "base64");
    const bb = Buffer.from(b, "base64");
    if (ab.length !== bb.length) return false;
    return crypto.timingSafeEqual(ab, bb);
  } catch {
    return false;
  }
}

function isCanonicalBase64(s: string): boolean {
  const buf = Buffer.from(s, "base64");
  return buf.toString("base64") === s;
}
