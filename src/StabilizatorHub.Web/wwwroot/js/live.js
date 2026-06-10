// SignalR real-time channel: telemetry, device status and voltage events.
let connection = null;

function setConnBadge(state, text) {
  const badge = document.getElementById('conn');
  if (!badge) return;
  badge.dataset.state = state;
  document.getElementById('conn-text').textContent = text;
}

export async function initLive({ onTelemetry, onStatus, onVoltageEvent }) {
  connection = new signalR.HubConnectionBuilder()
    .withUrl('/hub/live')
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .build();

  connection.on('telemetry', onTelemetry);
  connection.on('deviceStatus', onStatus);
  connection.on('voltageEvent', onVoltageEvent);

  connection.onreconnecting(() => setConnBadge('reconnecting', 'reconnecting...'));
  connection.onreconnected(() => setConnBadge('live', 'live'));
  connection.onclose(() => setConnBadge('offline', 'disconnected'));

  try {
    await connection.start();
    setConnBadge('live', 'live');
  } catch {
    setConnBadge('offline', 'disconnected');
    // Retry once after a few seconds; afterwards the polling fallback still works.
    setTimeout(() => connection.start()
      .then(() => setConnBadge('live', 'live'))
      .catch(() => {}), 5000);
  }
}

/** Subscribes this connection to a freshly claimed device. */
export function joinDevice(deviceId) {
  return connection?.invoke('JoinDevice', deviceId).catch(() => {});
}
