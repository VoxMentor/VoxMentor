import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
  HttpTransportType,
} from "@microsoft/signalr";

const HUBS = {
  tutor: "/hubs/tutor",
  mastery: "/hubs/mastery",
  interview: "/hubs/interview",
} as const;

type HubName = keyof typeof HUBS;

function createHubConnection(hubName: HubName): HubConnection {
  const connection = new HubConnectionBuilder()
    .withUrl(HUBS[hubName], {
      transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
      withCredentials: true,
    })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (retryContext) => {
        if (retryContext.elapsedMilliseconds > 60000) return null;
        return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
      },
    })
    .configureLogging(LogLevel.Information)
    .build();

  connection.onclose((error) => {
    if (error) {
      console.error(`[${hubName}] Connection closed with error:`, error);
    } else {
      console.log(`[${hubName}] Connection closed.`);
    }
  });

  connection.onreconnecting((error) => {
    console.warn(`[${hubName}] Reconnecting...`, error);
  });

  connection.onreconnected((connectionId) => {
    console.log(`[${hubName}] Reconnected. ConnectionId: ${connectionId}`);
  });

  return connection;
}

let connections: Record<HubName, HubConnection | null> = {
  tutor: null,
  mastery: null,
  interview: null,
};

export function getTutorConnection(): HubConnection {
  if (!connections.tutor) {
    connections.tutor = createHubConnection("tutor");
  }
  return connections.tutor;
}

export function getMasteryConnection(): HubConnection {
  if (!connections.mastery) {
    connections.mastery = createHubConnection("mastery");
  }
  return connections.mastery;
}

export function getInterviewConnection(): HubConnection {
  if (!connections.interview) {
    connections.interview = createHubConnection("interview");
  }
  return connections.interview;
}

export async function startAllConnections(): Promise<void> {
  const hubs = [getTutorConnection(), getMasteryConnection(), getInterviewConnection()];
  await Promise.all(
    hubs
      .filter((c) => c.state === HubConnectionState.Disconnected)
      .map((c) => c.start())
  );
}

export async function stopAllConnections(): Promise<void> {
  const hubs = [connections.tutor, connections.mastery, connections.interview];
  await Promise.all(
    hubs.filter(Boolean).map((c) => c!.stop())
  );
  connections = { tutor: null, mastery: null, interview: null };
}
