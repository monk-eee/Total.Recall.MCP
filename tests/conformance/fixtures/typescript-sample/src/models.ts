/** Shared models for the conformance fixture. */

export enum OrderStatus {
  Pending = "PENDING",
  Approved = "APPROVED",
  Shipped = "SHIPPED",
  Cancelled = "CANCELLED",
}

export interface User {
  id: number;
  email: string;
  displayName?: string;
}

export class Money {
  constructor(
    public readonly amount: number,
    public readonly currency: string = "USD",
  ) {}
}

export class Order {
  private _cachedTotal: number | null = null;

  constructor(
    public readonly orderId: string,
    public readonly user: User,
    public status: OrderStatus = OrderStatus.Pending,
  ) {}

  get cachedTotal(): number | null {
    return this._cachedTotal;
  }
}

class _InternalCache {
  entries: Map<string, unknown> = new Map();
}

export type OrderId = string;
