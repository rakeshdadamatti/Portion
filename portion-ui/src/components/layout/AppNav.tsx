import { NavLink } from 'react-router-dom';
import { FolderSync, MessageSquareText, UploadCloud, Users } from 'lucide-react';
import { cn } from '../../lib/cn';

interface NavItem {
  to: string;
  label: string;
  icon: typeof MessageSquareText;
}

const NAV_ITEMS: readonly NavItem[] = [
  { to: '/screening', label: 'Interactive Chat', icon: MessageSquareText },
  { to: '/resumes', label: 'Resumes', icon: Users },
  { to: '/upload', label: 'Direct Upload', icon: UploadCloud },
  { to: '/sync', label: 'Bulk Folder Sync', icon: FolderSync },
];

export function AppNav() {
  return (
    <nav className="header-nav" aria-label="Primary">
      {NAV_ITEMS.map((item) => (
        <NavLink
          key={item.to}
          to={item.to}
          className={({ isActive }) => cn('nav-btn', isActive && 'nav-btn--active')}
        >
          <item.icon size={14} aria-hidden="true" className="nav-btn__icon" />
          <span>{item.label}</span>
        </NavLink>
      ))}
    </nav>
  );
}
