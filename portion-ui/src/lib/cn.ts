import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

/** Conditional class composition used by every component in the app. */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}
