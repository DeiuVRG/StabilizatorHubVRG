// Chart.js setup: live voltage line chart + consumption bar chart.
import { api, tzOffsetMinutes } from './api.js';

const MAX_LIVE_POINTS = 60;

let liveChart = null;
let historyChart = null;

function baseOptions() {
  Chart.defaults.color = '#8b9bab';
  Chart.defaults.borderColor = 'rgba(36, 49, 64, 0.8)';
  Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
}

export function initCharts() {
  baseOptions();

  liveChart = new Chart(document.getElementById('live-chart'), {
    type: 'line',
    data: {
      labels: [],
      datasets: [
        {
          label: 'Input V',
          data: [],
          borderColor: '#e8b23e',
          backgroundColor: 'rgba(232, 178, 62, 0.08)',
          borderWidth: 2,
          pointRadius: 0,
          tension: 0.3,
          fill: false
        },
        {
          label: 'Output V',
          data: [],
          borderColor: '#4f9cf9',
          backgroundColor: 'rgba(79, 156, 249, 0.08)',
          borderWidth: 2,
          pointRadius: 0,
          tension: 0.3,
          fill: false
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      interaction: { mode: 'index', intersect: false },
      scales: {
        y: { suggestedMin: 200, suggestedMax: 250, ticks: { callback: v => v + ' V' } },
        x: { ticks: { maxTicksLimit: 8 } }
      },
      plugins: { legend: { labels: { boxWidth: 18 } } }
    }
  });

  historyChart = new Chart(document.getElementById('history-chart'), {
    type: 'bar',
    data: {
      labels: [],
      datasets: [{
        label: 'Energy (kWh)',
        data: [],
        backgroundColor: 'rgba(79, 156, 249, 0.55)',
        borderColor: '#4f9cf9',
        borderWidth: 1,
        borderRadius: 4,
        maxBarThickness: 42
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      scales: {
        y: { beginAtZero: true, ticks: { callback: v => v + ' kWh' } },
        x: { ticks: { maxTicksLimit: 16 } }
      },
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            afterBody(items) {
              const bucket = items[0]?.raw?.bucket;
              if (!bucket || !bucket.sampleCount) return '';
              return [
                `Avg power: ${Math.round(bucket.avgPowerW)} W`,
                `Input voltage: ${Math.round(bucket.minVoltageIn)}-${Math.round(bucket.maxVoltageIn)} V`
              ];
            }
          }
        }
      }
    }
  });
}

export function seedLive(samples) {
  liveChart.data.labels = samples.map(s => shortTime(s.timestampUtc));
  liveChart.data.datasets[0].data = samples.map(s => s.voltageIn);
  liveChart.data.datasets[1].data = samples.map(s => s.voltageOut);
  liveChart.update();
}

export function pushLivePoint(sample) {
  liveChart.data.labels.push(shortTime(sample.timestampUtc));
  liveChart.data.datasets[0].data.push(sample.voltageIn);
  liveChart.data.datasets[1].data.push(sample.voltageOut);

  while (liveChart.data.labels.length > MAX_LIVE_POINTS) {
    liveChart.data.labels.shift();
    liveChart.data.datasets.forEach(d => d.data.shift());
  }

  liveChart.update();
}

export function clearLive() {
  liveChart.data.labels = [];
  liveChart.data.datasets.forEach(d => { d.data = []; });
  liveChart.update();
}

export async function refreshHistory(deviceId, range) {
  const buckets = await api(`/api/devices/${deviceId}/history?range=${range}&tz=${tzOffsetMinutes}`);

  historyChart.data.labels = buckets.map(b => prettyLabel(b.label, range));
  historyChart.data.datasets[0].data = buckets.map(b => ({
    x: prettyLabel(b.label, range),
    y: b.energyKwh,
    bucket: b
  }));
  historyChart.update();
}

function shortTime(iso) {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

// Backend labels are local-time strings: "yyyy-MM-dd HH:00" | "yyyy-MM-dd" | "yyyy-MM".
function prettyLabel(label, range) {
  if (range === 'day') return label.slice(11);

  if (range === 'year') {
    const [year, month] = label.split('-').map(Number);
    return `${MONTHS[month - 1]} ${year}`;
  }

  const [, month, day] = label.split('-').map(Number);
  return `${day} ${MONTHS[month - 1]}`;
}
