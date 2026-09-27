import { useRef, useState, type ReactNode } from 'react';
import { ChevronDown } from 'lucide-react';
import { useClickOutside } from '../../../hooks/useClickOutside';
import { cn } from '../../../lib/cn';

export interface QuickPromptItem {
  id: string;
  label: string;
}

export interface QuickPromptMenuProps {
  id: string;
  label: string;
  icon?: ReactNode;
  items: readonly QuickPromptItem[];
  onSelect: (item: QuickPromptItem) => void;
  disabled?: boolean;
}

/** Pill + popover list used by the composer's quick-action row. */
export function QuickPromptMenu({ id, label, icon, items, onSelect, disabled = false }: QuickPromptMenuProps) {
  const [isOpen, setIsOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const menuId = `${id}-menu`;

  useClickOutside(containerRef, () => setIsOpen(false), isOpen);

  return (
    <div className="pill-dropdown" ref={containerRef}>
      <button
        type="button"
        className={cn('pill', isOpen && 'pill--open')}
        onClick={() => setIsOpen((open) => !open)}
        disabled={disabled}
        aria-haspopup="menu"
        aria-expanded={isOpen}
        aria-controls={isOpen ? menuId : undefined}
      >
        {icon}
        <span>{label}</span>
        <ChevronDown size={12} aria-hidden="true" />
      </button>

      {isOpen ? (
        <div className="pill-menu" id={menuId} role="menu">
          {items.map((item) => (
            <button
              key={item.id}
              type="button"
              role="menuitem"
              className="pill-menu-item"
              onClick={() => {
                setIsOpen(false);
                onSelect(item);
              }}
            >
              {item.label}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
