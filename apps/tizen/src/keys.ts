const MEDIA_KEYS = [
  "MediaPlay",
  "MediaPause",
  "MediaPlayPause",
  "MediaStop",
  "MediaFastForward",
  "MediaRewind",
  "ColorF0Red",
];

export function registerRemoteKeys(): void {
  const device = window.tizen?.tvinputdevice;
  if (!device) return;
  const supported = new Set(device.getSupportedKeys().map((key) => key.name));
  for (const name of MEDIA_KEYS) {
    if (supported.has(name)) device.registerKey(name);
  }
}

export function keyDirection(code: number): "left" | "right" | "up" | "down" | null {
  if (code === 37) return "left";
  if (code === 39) return "right";
  if (code === 38) return "up";
  if (code === 40) return "down";
  return null;
}

export function isEnter(code: number): boolean {
  return code === 13;
}

export function isBack(code: number): boolean {
  return code === 10009 || code === 8 || code === 461;
}

export function isPlayPause(code: number): boolean {
  return code === 10252 || code === 415 || code === 19 || code === 32;
}
