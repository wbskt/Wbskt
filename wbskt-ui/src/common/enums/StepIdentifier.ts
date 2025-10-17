export const StepIdentifier = {
  // Actions
  ActionLog: 'ActionLog',
  ActionSendEmail: 'ActionSendEmail',
  ActionMakeHttpRequest: 'ActionMakeHttpRequest',
  ActionSendPayloadToClient: 'ActionSendPayloadToClient',

  // Modifiers
  ModifierIfCondition: 'ModifierIfCondition',

  // Triggers
  Timed: 'Timed',
  Webhook: 'Webhook',
  ClientData: 'ClientData',
} as const;

export type StepIdentifier = typeof StepIdentifier[keyof typeof StepIdentifier];
