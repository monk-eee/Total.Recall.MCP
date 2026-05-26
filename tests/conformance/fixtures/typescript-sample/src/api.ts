import { Order, User, OrderStatus } from "./models.js";

export interface UserRepo {
  get(id: number): User | undefined;
  save(user: User): void;
}

export interface OrderRepo<T = Order> {
  find(id: string): T | undefined;
  persist(entity: T): void;
}

export abstract class OrderServiceBase {
  abstract createOrder(user: User): Order;
}

export class OrderService extends OrderServiceBase implements UserRepo {
  constructor(
    private readonly users: UserRepo,
    private readonly orders: OrderRepo,
    public readonly defaultCurrency: string = "USD",
  ) {
    super();
  }

  override createOrder(user: User): Order {
    return new Order(crypto.randomUUID(), user, OrderStatus.Pending);
  }

  get(id: number): User | undefined {
    return this.users.get(id);
  }

  save(user: User): void {
    this.users.save(user);
  }
}

export function calculateDiscount(amount: number, percent: number = 10.0): number {
  return Math.floor(amount * (1 - percent / 100));
}
