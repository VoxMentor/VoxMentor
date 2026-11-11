/**
 * Transcript helpers for the mock interview page (#129). The interviewer's
 * tokens arrive one at a time over the hub, so the transcript is built by
 * appending to the turn the stream is currently filling.
 */

export interface InterviewTurn {
  id: number;
  role: "interviewer" | "candidate";
  text: string;
}

/**
 * Appends a streamed token to the turn the stream is currently filling.
 * A token for an unknown turn is dropped rather than creating a new one —
 * a late token must never leak into the transcript as a stray bubble.
 */
export function appendToken(
  turns: InterviewTurn[],
  targetId: number,
  token: string
): InterviewTurn[] {
  if (!turns.some((turn) => turn.id === targetId)) return turns;
  return turns.map((turn) =>
    turn.id === targetId ? { ...turn, text: turn.text + token } : turn
  );
}

/**
 * The opening normally streams in token by token. When nothing arrived the
 * start result's opening message is the transcript's first turn instead.
 */
export function openingTurns(
  turns: InterviewTurn[],
  id: number,
  openingMessage: string
): InterviewTurn[] {
  if (turns.some((turn) => turn.text)) return turns;
  return [{ id, role: "interviewer", text: openingMessage }];
}
