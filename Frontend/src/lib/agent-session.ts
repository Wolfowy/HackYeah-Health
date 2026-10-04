import type { AgentAccess, AgentApi } from './agent-api'

/** One transport session can emit both SDK disconnect and local cleanup events. */
export class AgentSession {
  conversationId: string | null = null
  private binding: Promise<void> | null = null
  private ending: Promise<void> | null = null

  constructor(
    private api: AgentApi,
    private access: AgentAccess,
    readonly id: string,
  ) {}

  bind(conversationId: string) {
    if (!conversationId || (this.conversationId && this.conversationId !== conversationId))
      return Promise.reject(new Error('Asystent zwrócił niezgodny identyfikator rozmowy.'))
    this.conversationId = conversationId
    // Persist correlation even if the screen closes while the SDK connects.
    this.binding ??= this.api.bindConversation(this.access, this.id, conversationId)
    return this.binding
  }

  end(continuesInterview = false) {
    this.ending ??= (async () => {
      // A disconnect can arrive before the startSession promise resolves.
      await this.binding?.catch(() => {})
      await this.api.endSession(this.access, this.id, continuesInterview)
    })()
    return this.ending
  }
}
