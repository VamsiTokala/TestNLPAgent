export interface AssistantQueryRequest { query: string; userContext: { userId: string; roles: string[]; legalEntities: string[]; }; }
