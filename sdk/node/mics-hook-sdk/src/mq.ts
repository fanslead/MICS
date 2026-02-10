import { verifyMqEventSign } from "./signing.js";
import { MqEventCodec, type MqEvent } from "./proto/mics_hook.js";

export function eventTopicName(tenantId: string): string {
  return `im-mics-${tenantId}-event`;
}

export function dlqTopicName(tenantId: string): string {
  return `im-mics-${tenantId}-event-dlq`;
}

export function decodeMqEvent(bytes: Uint8Array): MqEvent {
  return MqEventCodec.decode(bytes);
}

export function isValidMqEventSign(tenantSecret: string, evt: MqEvent): boolean {
  if (!evt.sign) return false;
  return verifyMqEventSign(tenantSecret, evt, evt.sign);
}

export function decodeAndVerifyMqEvent(
  bytes: Uint8Array,
  options: { tenantSecret: string; requireSign: boolean }
): { ok: true; event: MqEvent; reason: "" } | { ok: false; event: MqEvent; reason: string } {
  const event = decodeMqEvent(bytes);

  if (!options.requireSign && !event.sign) {
    return { ok: true, event, reason: "" };
  }

  if (!event.sign) {
    return { ok: false, event, reason: "invalid sign" };
  }

  return isValidMqEventSign(options.tenantSecret, event)
    ? { ok: true, event, reason: "" }
    : { ok: false, event, reason: "invalid sign" };
}
