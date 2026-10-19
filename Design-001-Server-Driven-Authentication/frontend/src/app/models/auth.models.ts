// Mirrors the backend contracts (backend/src/AuthPolicyEngine.Api/Contracts/Contracts.cs).
// `type` is a plain string on purpose: the server may introduce new challenge types
// that this client has never heard of, and the UI must degrade gracefully.

export interface IdentifyRequest {
  identifier: string;
  region?: string;
}

export interface SelectChallengeRequest {
  authenticationType: string;
}

export interface Challenge {
  type: string;
  displayName: string;
  description: string;
}

/** flowId, expiresAt and selectedChallenge are null when the account has no eligible method. */
export interface ChallengeResponse {
  flowId: string | null;
  expiresAt: string | null;
  selectedChallenge: Challenge | null;
  alternatives: Challenge[];
  notice: string;
}

/** RFC 7807 problem details as returned by the backend. */
export interface ProblemDetails {
  status?: number;
  title?: string;
  detail?: string;
}

export type ApiErrorKind =
  | 'invalid-request'
  | 'unknown-account'
  | 'unknown-flow'
  | 'method-not-allowed'
  | 'flow-expired'
  | 'backend-unavailable'
  | 'unexpected-response'
  | 'unexpected-error';

export interface ApiError {
  kind: ApiErrorKind;
  message: string;
  /** True when the only sensible next step is to start a new flow. */
  restartRequired: boolean;
}
